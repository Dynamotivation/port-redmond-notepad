using System.Text;
using Notepads.Utilities;

namespace Redmond.Notepad.Core;

public sealed record DocumentFileReference
{
    public DocumentFileReference(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = System.IO.Path.GetFullPath(path);
    }

    public string Path { get; }

    public string DisplayName => System.IO.Path.GetFileName(Path);
}

public sealed record TextFileMetadata(
    Encoding Encoding,
    LineEnding LineEnding,
    DateTimeOffset? LastModified = null)
{
    public static TextFileMetadata NewDocument { get; } = new(
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        LineEnding.Crlf);
}

public interface ITextFileStore
{
    Task<TextFileMetadata> LoadAsync(
        DocumentFileReference file,
        ITextBuffer destination,
        Encoding? requestedEncoding = null,
        CancellationToken cancellationToken = default);

    Task<TextFileMetadata> SaveAsync(
        DocumentFileReference file,
        ITextBuffer source,
        TextFileMetadata metadata,
        CancellationToken cancellationToken = default);
}

public interface INotepadFilePicker
{
    Task<IReadOnlyList<DocumentFileReference>> PickOpenFilesAsync(
        bool allowMultiple,
        CancellationToken cancellationToken = default);

    Task<DocumentFileReference?> PickSaveFileAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default);
}
