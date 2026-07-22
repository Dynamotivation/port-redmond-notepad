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
    DateTimeOffset? LastModified = null,
    FileContentVersion? Version = null)
{
    public static TextFileMetadata NewDocument { get; } = new(
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        LineEnding.Crlf);
}

public sealed record FileContentVersion(
    long Length,
    DateTimeOffset LastModified,
    string Sha256)
{
    public bool HasSameContent(FileContentVersion? other) =>
        other is not null
        && Length == other.Length
        && string.Equals(Sha256, other.Sha256, StringComparison.Ordinal);
}

public enum ExternalFileChange
{
    None,
    Modified,
    Deleted,
}

public sealed record TextFileSaveOptions(
    FileContentVersion? ExpectedVersion = null,
    bool OverwriteExternalChanges = false);

public sealed class FileChangedExternallyException : IOException
{
    public FileChangedExternallyException(DocumentFileReference file, ExternalFileChange change)
        : base(change == ExternalFileChange.Deleted
            ? $"'{file.DisplayName}' was deleted or moved after it was opened."
            : $"'{file.DisplayName}' was changed by another application after it was opened.")
    {
        File = file;
        Change = change;
    }

    public DocumentFileReference File { get; }

    public ExternalFileChange Change { get; }
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
        TextFileSaveOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<FileContentVersion?> GetVersionAsync(
        DocumentFileReference file,
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
