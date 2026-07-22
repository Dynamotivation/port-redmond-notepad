using Notepads.Utilities;
using System.Text;

namespace Redmond.Notepad.Core;

public sealed class NotepadDocument
{
    public const string UntitledName = "Untitled";

    public NotepadDocument(ITextBuffer buffer)
    {
        Buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
    }

    public ITextBuffer Buffer { get; }

    public string Text => Buffer.Text;

    public DocumentFileReference? File { get; private set; }

    public string DisplayName => File?.DisplayName ?? UntitledName;

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
}
