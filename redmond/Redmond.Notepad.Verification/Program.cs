using System.Text;
using Notepads.Utilities;
using Redmond.Notepad.Core;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var verificationDirectory = Path.Combine(
    Path.GetTempPath(),
    $"redmond-notepad-verification-{Guid.NewGuid():N}");
Directory.CreateDirectory(verificationDirectory);

try
{
    var store = new PhysicalTextFileStore();
    await VerifyRoundTripAsync(
        store,
        verificationDirectory,
        "utf8-no-bom.txt",
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        LineEnding.Lf,
        "alpha\r\nbeta\rgamma\n✓",
        "alpha\nbeta\ngamma\n✓",
        expectedPreamble: []);
    await VerifyRoundTripAsync(
        store,
        verificationDirectory,
        "utf8-bom.txt",
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
        LineEnding.Crlf,
        "eins\nzwei\r三",
        "eins\r\nzwei\r\n三",
        expectedPreamble: [0xEF, 0xBB, 0xBF]);
    await VerifyRoundTripAsync(
        store,
        verificationDirectory,
        "utf16-be.txt",
        new UnicodeEncoding(bigEndian: true, byteOrderMark: true),
        LineEnding.Cr,
        "one\r\ntwo\nthree",
        "one\rtwo\rthree",
        expectedPreamble: [0xFE, 0xFF]);

    Console.WriteLine("Core file verification: passed");
}
finally
{
    Directory.Delete(verificationDirectory, recursive: true);
}

static async Task VerifyRoundTripAsync(
    ITextFileStore store,
    string directory,
    string fileName,
    Encoding encoding,
    LineEnding lineEnding,
    string sourceText,
    string expectedText,
    byte[] expectedPreamble)
{
    var path = Path.Combine(directory, fileName);
    await File.WriteAllTextAsync(path, "stale content");
    var file = new DocumentFileReference(path);
    var source = new StringTextBuffer(sourceText);
    var snapshot = source.CreateSnapshot();
    source.Replace(0, 0, "changed-");
    Assert(source.IsModified, $"{fileName}: edits must mark the buffer modified");
    Assert(snapshot.Text == sourceText, $"{fileName}: immutable snapshot changed with the live buffer");
    source.Replace(0, "changed-".Length, string.Empty);

    await store.SaveAsync(file, source, new TextFileMetadata(encoding, lineEnding));
    Assert(!source.IsModified, $"{fileName}: saving must mark the buffer original");

    var bytes = await File.ReadAllBytesAsync(path);
    Assert(
        bytes.AsSpan(0, expectedPreamble.Length).SequenceEqual(expectedPreamble),
        $"{fileName}: preamble mismatch");
    Assert(
        !Directory.EnumerateFiles(directory, $".{fileName}.*.tmp").Any(),
        $"{fileName}: atomic-save temporary file was not cleaned up");

    var destination = new StringTextBuffer();
    var loaded = await store.LoadAsync(file, destination);
    Assert(destination.Text == expectedText, $"{fileName}: text or line endings did not round-trip");
    Assert(destination.LineCount == CountLines(expectedText),
        $"{fileName}: line count mismatch");
    Assert(!destination.IsModified, $"{fileName}: loading must mark the buffer original");
    Assert(loaded.LineEnding == lineEnding, $"{fileName}: line-ending metadata mismatch");
    Assert(
        TextEncoding.GetDisplayName(loaded.Encoding) == TextEncoding.GetDisplayName(encoding),
        $"{fileName}: encoding metadata mismatch");
}

static int CountLines(string text)
{
    var count = 1;
    for (var index = 0; index < text.Length; index++)
    {
        if (text[index] == '\r')
        {
            count++;
            if (index + 1 < text.Length && text[index + 1] == '\n')
            {
                index++;
            }
        }
        else if (text[index] == '\n')
        {
            count++;
        }
    }

    return count;
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
