using Notepads.Utilities;
using System.Text;

namespace Redmond.Notepad.Core;

public sealed class NotepadDocument
{
    public const string UntitledName = "Untitled";
    private const int MaximumPreviewLength = 120;

    public NotepadDocument(ITextBuffer buffer)
    {
        Buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
    }

    public ITextBuffer Buffer { get; }

    public string Text => Buffer.Text;

    public DocumentFileReference? File { get; private set; }

    public string DisplayName => File?.DisplayName ?? GetUntitledPreview();

    public string SuggestedFileName
    {
        get
        {
            if (File is not null)
            {
                return File.DisplayName;
            }

            var preview = GetUntitledPreview();
            return preview.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                ? preview
                : $"{preview}.txt";
        }
    }

    public Encoding Encoding { get; private set; } = TextFileMetadata.NewDocument.Encoding;

    public string EncodingDisplayText => TextEncoding.GetDisplayName(Encoding);

    public LineEnding LineEnding { get; private set; } = LineEnding.Crlf;

    public string LineEndingDisplayText => LineEndingUtility.GetLineEndingDisplayText(LineEnding);

    public int CharacterCount => Buffer.Length;

    public int LineCount => Buffer.LineCount;

    public bool IsModified => Buffer.IsModified;

    public ITextSnapshot CreateSnapshot() => Buffer.CreateSnapshot();

    public void LoadText(string? text)
    {
        var loadedText = text ?? string.Empty;
        LineEnding = LineEndingUtility.GetLineEndingTypeFromText(loadedText);
        Buffer.Text = loadedText;
        Buffer.MarkAsOriginal();
    }

    public async Task LoadAsync(
        ITextFileStore store,
        DocumentFileReference file,
        Encoding? requestedEncoding = null,
        CancellationToken cancellationToken = default)
    {
        var metadata = await store.LoadAsync(file, Buffer, requestedEncoding, cancellationToken);
        File = file;
        Encoding = metadata.Encoding;
        LineEnding = metadata.LineEnding;
    }

    public async Task SaveAsync(
        ITextFileStore store,
        DocumentFileReference? destination = null,
        CancellationToken cancellationToken = default)
    {
        var file = destination ?? File
            ?? throw new InvalidOperationException("A destination is required for an untitled document.");
        var metadata = new TextFileMetadata(Encoding, LineEnding);
        metadata = await store.SaveAsync(file, Buffer, metadata, cancellationToken);
        File = file;
        Encoding = metadata.Encoding;
        LineEnding = metadata.LineEnding;
    }

    private string GetUntitledPreview()
    {
        using var reader = Buffer.CreateReader();
        var preview = new StringBuilder(MaximumPreviewLength);
        var lineHasContent = false;

        while (reader.Read() is var value && value >= 0)
        {
            var character = (char)value;
            if (character is '\r' or '\n')
            {
                if (lineHasContent)
                {
                    break;
                }

                preview.Clear();
                continue;
            }

            if (!lineHasContent && char.IsWhiteSpace(character))
            {
                continue;
            }

            lineHasContent = true;
            if (preview.Length < MaximumPreviewLength)
            {
                preview.Append(character);
            }

            if (preview.Length == MaximumPreviewLength)
            {
                break;
            }
        }

        if (!lineHasContent)
        {
            return UntitledName;
        }

        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(preview
            .ToString()
            .Where(character => !char.IsControl(character) && !invalidCharacters.Contains(character))
            .ToArray())
            .Trim();

        if (OperatingSystem.IsWindows())
        {
            sanitized = sanitized.TrimEnd(' ', '.');
            var baseName = Path.GetFileNameWithoutExtension(sanitized);
            if (WindowsReservedFileNames.Contains(baseName))
            {
                sanitized = $"_{sanitized}";
            }
        }

        return string.IsNullOrWhiteSpace(sanitized) || sanitized is "." or ".."
            ? UntitledName
            : sanitized;
    }

    private static readonly HashSet<string> WindowsReservedFileNames = new(
        [
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        ],
        StringComparer.OrdinalIgnoreCase);
}
