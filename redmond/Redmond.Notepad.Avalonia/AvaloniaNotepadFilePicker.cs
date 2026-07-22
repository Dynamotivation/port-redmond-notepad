using Avalonia.Platform.Storage;
using Redmond.Notepad.Core;

namespace Redmond.Notepad.Avalonia;

internal sealed class AvaloniaNotepadFilePicker(IStorageProvider storageProvider) : INotepadFilePicker
{
    private static readonly FilePickerFileType TextDocuments = new("Text documents")
    {
        Patterns = ["*.txt", "*.md", "*.log", "*.json", "*.xml", "*.csv", "*.srt", "*.ass", "*.lrc", "*.lic"],
        MimeTypes = ["text/plain"],
    };

    public async Task<IReadOnlyList<DocumentFileReference>> PickOpenFilesAsync(
        bool allowMultiple,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!storageProvider.CanOpen)
        {
            return [];
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open",
            AllowMultiple = allowMultiple,
            FileTypeFilter = [TextDocuments, FilePickerFileTypes.All],
        });

        return files
            .Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => new DocumentFileReference(path!))
            .ToArray();
    }

    public async Task<DocumentFileReference?> PickSaveFileAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!storageProvider.CanSave)
        {
            return null;
        }

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save As",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "txt",
            FileTypeChoices = [TextDocuments, FilePickerFileTypes.All],
        });
        var path = file?.TryGetLocalPath();
        return string.IsNullOrWhiteSpace(path) ? null : new DocumentFileReference(path);
    }
}
