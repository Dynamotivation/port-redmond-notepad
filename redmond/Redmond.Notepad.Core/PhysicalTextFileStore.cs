using System.Text;
using System.Security.Cryptography;
using UtfUnknown;

namespace Redmond.Notepad.Core;

public sealed class PhysicalTextFileStore : ITextFileStore
{
    private const int CopyBufferSize = 16 * 1024;

    static PhysicalTextFileStore() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public async Task<TextFileMetadata> LoadAsync(
        DocumentFileReference file,
        ITextBuffer destination,
        Encoding? requestedEncoding = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(destination);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using var stream = OpenRead(file.Path);
            var initialLength = stream.Length;
            var initialModified = File.GetLastWriteTimeUtc(file.Path);
            var encoding = requestedEncoding ?? DetectEncoding(stream);
            var hasPreamble = HasMatchingPreamble(stream, encoding);
            stream.Position = 0;

            string text;
            string hash;
            using (var hasher = SHA256.Create())
            {
                await using var hashingStream = new CryptoStream(
                    stream,
                    hasher,
                    CryptoStreamMode.Read,
                    leaveOpen: true);
                using var reader = new StreamReader(
                    hashingStream,
                    encoding,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: CopyBufferSize,
                    leaveOpen: false);
                text = await reader.ReadToEndAsync(cancellationToken);
                encoding = PreservePreamblePreference(reader.CurrentEncoding, hasPreamble);
                hash = Convert.ToHexString(hasher.Hash
                    ?? throw new IOException($"Could not fingerprint '{file.DisplayName}'."));
            }

            var finalLength = stream.Length;
            var finalModified = File.GetLastWriteTimeUtc(file.Path);
            if (initialLength != finalLength || initialModified != finalModified)
            {
                if (attempt < 2)
                {
                    continue;
                }

                throw new IOException($"'{file.DisplayName}' kept changing while it was being opened.");
            }

            destination.Text = text;
            destination.MarkAsOriginal();
            var version = new FileContentVersion(finalLength, finalModified, hash);
            return new TextFileMetadata(
                encoding,
                LineEndingUtility.GetLineEndingTypeFromText(text),
                finalModified,
                version);
        }

        throw new IOException($"Could not read a stable version of '{file.DisplayName}'.");
    }

    public async Task<TextFileMetadata> SaveAsync(
        DocumentFileReference file,
        ITextBuffer source,
        TextFileMetadata metadata,
        TextFileSaveOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(metadata);
        options ??= new TextFileSaveOptions();

        await EnsureExpectedVersionAsync(file, options, cancellationToken);

        var directory = Path.GetDirectoryName(file.Path)
            ?? throw new IOException("The destination does not have a parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(file.Path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                CopyBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var writer = new StreamWriter(stream, metadata.Encoding, CopyBufferSize, leaveOpen: false))
            using (var reader = source.CreateReader())
            {
                await CopyWithLineEndingAsync(reader, writer, metadata.LineEnding, cancellationToken);
                await writer.FlushAsync(cancellationToken);
            }

            // Recheck after rendering the temporary file. This narrows the race
            // window and prevents a long save from overwriting an intervening edit.
            await EnsureExpectedVersionAsync(file, options, cancellationToken);
            File.Move(temporaryPath, file.Path, overwrite: true);
            var version = await GetVersionAsync(file, cancellationToken)
                ?? throw new IOException($"'{file.DisplayName}' disappeared immediately after it was saved.");
            source.MarkAsOriginal();
            return metadata with { LastModified = version.LastModified, Version = version };
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task<FileContentVersion?> GetVersionAsync(
        DocumentFileReference file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await using var stream = OpenRead(file.Path);
                var initialLength = stream.Length;
                var initialModified = File.GetLastWriteTimeUtc(file.Path);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
                var finalLength = stream.Length;
                var finalModified = File.GetLastWriteTimeUtc(file.Path);
                if (initialLength == finalLength && initialModified == finalModified)
                {
                    return new FileContentVersion(finalLength, finalModified, hash);
                }
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                return null;
            }

            if (attempt == 2)
            {
                throw new IOException($"'{file.DisplayName}' kept changing while it was being checked.");
            }
        }

        return null;
    }

    private async Task EnsureExpectedVersionAsync(
        DocumentFileReference file,
        TextFileSaveOptions options,
        CancellationToken cancellationToken)
    {
        if (options.ExpectedVersion is null || options.OverwriteExternalChanges)
        {
            return;
        }

        var current = await GetVersionAsync(file, cancellationToken);
        if (current is null)
        {
            throw new FileChangedExternallyException(file, ExternalFileChange.Deleted);
        }

        if (!options.ExpectedVersion.HasSameContent(current))
        {
            throw new FileChangedExternallyException(file, ExternalFileChange.Modified);
        }
    }

    private static FileStream OpenRead(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete,
        CopyBufferSize,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static Encoding DetectEncoding(Stream stream)
    {
        Span<byte> bom = stackalloc byte[4];
        var count = stream.Read(bom);
        stream.Position = 0;

        if (count >= 4 && bom[..4].SequenceEqual(new byte[] { 0x00, 0x00, 0xFE, 0xFF }))
        {
            return new UTF32Encoding(bigEndian: true, byteOrderMark: true, throwOnInvalidCharacters: true);
        }

        if (count >= 4 && bom[..4].SequenceEqual(new byte[] { 0xFF, 0xFE, 0x00, 0x00 }))
        {
            return new UTF32Encoding(bigEndian: false, byteOrderMark: true, throwOnInvalidCharacters: true);
        }

        if (count >= 3 && bom[..3].SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);
        }

        if (count >= 2 && bom[..2].SequenceEqual(new byte[] { 0xFE, 0xFF }))
        {
            return new UnicodeEncoding(bigEndian: true, byteOrderMark: true, throwOnInvalidBytes: true);
        }

        if (count >= 2 && bom[..2].SequenceEqual(new byte[] { 0xFF, 0xFE }))
        {
            return new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true);
        }

        var detection = CharsetDetector.DetectFromStream(stream);
        stream.Position = 0;
        var detected = detection.Detected?.Encoding;
        return detected is null || detected.CodePage == Encoding.ASCII.CodePage
            ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
            : PreservePreamblePreference(detected, hasPreamble: false);
    }

    private static bool HasMatchingPreamble(Stream stream, Encoding encoding)
    {
        var preamble = encoding.GetPreamble();
        if (preamble.Length == 0 || stream.Length < preamble.Length)
        {
            return false;
        }

        var position = stream.Position;
        stream.Position = 0;
        var actual = new byte[preamble.Length];
        var count = stream.Read(actual, 0, actual.Length);
        stream.Position = position;
        return count == actual.Length && actual.AsSpan().SequenceEqual(preamble);
    }

    private static Encoding PreservePreamblePreference(Encoding encoding, bool hasPreamble) => encoding switch
    {
        UTF8Encoding => new UTF8Encoding(hasPreamble, throwOnInvalidBytes: true),
        UnicodeEncoding => new UnicodeEncoding(encoding.CodePage == 1201, hasPreamble, throwOnInvalidBytes: true),
        UTF32Encoding => new UTF32Encoding(encoding.CodePage == 12001, hasPreamble, throwOnInvalidCharacters: true),
        _ => encoding,
    };

    private static async Task CopyWithLineEndingAsync(
        TextReader reader,
        TextWriter writer,
        LineEnding lineEnding,
        CancellationToken cancellationToken)
    {
        var target = lineEnding switch
        {
            LineEnding.Cr => "\r",
            LineEnding.Lf => "\n",
            _ => "\r\n",
        };
        var input = new char[CopyBufferSize];
        var output = new StringBuilder(CopyBufferSize + 2);
        var pendingCarriageReturn = false;

        while (true)
        {
            var count = await reader.ReadAsync(input.AsMemory(), cancellationToken);
            if (count == 0)
            {
                break;
            }

            output.Clear();
            for (var index = 0; index < count; index++)
            {
                var character = input[index];
                if (character == '\r')
                {
                    if (pendingCarriageReturn)
                    {
                        output.Append(target);
                    }

                    pendingCarriageReturn = true;
                    continue;
                }

                if (character == '\n')
                {
                    output.Append(target);
                    pendingCarriageReturn = false;
                    continue;
                }

                if (pendingCarriageReturn)
                {
                    output.Append(target);
                    pendingCarriageReturn = false;
                }

                output.Append(character);
            }

            await writer.WriteAsync(output.ToString().AsMemory(), cancellationToken);
        }

        if (pendingCarriageReturn)
        {
            await writer.WriteAsync(target.AsMemory(), cancellationToken);
        }
    }
}
