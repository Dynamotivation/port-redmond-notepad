using System.Text;
using System.Text.Json;

namespace Redmond.Notepad.Core;

public sealed record NotepadSessionState(NotepadSessionWindowState[] Windows)
{
    public static NotepadSessionState Empty { get; } = new([]);
}

public sealed record NotepadSessionWindowState(
    Guid Id,
    NotepadSessionDocumentState[] Documents,
    int SelectedIndex,
    long Revision = 0);

public sealed record NotepadSessionDocumentState(
    string? Path,
    string? ModifiedText,
    string? EncodingName = null,
    bool HasByteOrderMark = false,
    LineEnding LineEnding = LineEnding.Crlf,
    FileContentVersion? BaseFileVersion = null,
    Guid Id = default,
    string? RecoveryContentFile = null,
    bool HasRecoveryContent = false,
    NotepadTabEditorSessionState? EditorState = null);

public sealed record NotepadTabEditorSessionState(
    int CaretOffset,
    int SelectionStart,
    int SelectionLength,
    double HorizontalOffset,
    double VerticalOffset);

public sealed record NotepadSessionDocumentSnapshot(
    NotepadSessionDocumentState State,
    ITextSnapshot? ModifiedContent);

public sealed record NotepadSessionWindowSnapshot(
    Guid Id,
    NotepadSessionDocumentSnapshot[] Documents,
    int SelectedIndex,
    long Revision);

/// <summary>
/// Crash-tolerant storage for the open window/tab set. The manifest is replaced
/// atomically and modified buffers are stored separately so a large document
/// does not have to be encoded into the JSON manifest after every edit.
/// </summary>
public sealed class NotepadSessionRecoveryStore
{
    private const string ManifestFileName = "session.json";
    private const string DocumentsDirectoryName = "session-documents";
    private readonly object _sync = new();
    private readonly string _directory;
    private readonly string _manifestPath;
    private readonly string _documentsDirectory;
    private readonly Dictionary<Guid, long> _latestRevisions = [];

    public NotepadSessionRecoveryStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
        _manifestPath = Path.Combine(_directory, ManifestFileName);
        _documentsDirectory = Path.Combine(_directory, DocumentsDirectoryName);
    }

    public NotepadSessionState Load()
    {
        lock (_sync)
        {
            var persisted = LoadManifest();
            var windows = persisted.Windows
                .Select(window => window with
                {
                    Documents = (window.Documents ?? [])
                        .Select(HydrateDocument)
                        .ToArray(),
                })
                .ToArray();
            return new NotepadSessionState(windows);
        }
    }

    public void SaveWindow(NotepadSessionWindowSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_sync)
        {
            if (IsStale(snapshot.Id, snapshot.Revision))
            {
                return;
            }

            Directory.CreateDirectory(_documentsDirectory);
            var documents = snapshot.Documents
                .Select(document => PersistDocument(
                    snapshot.Id,
                    snapshot.Revision,
                    document))
                .ToArray();
            var window = new NotepadSessionWindowState(
                snapshot.Id,
                documents,
                Math.Clamp(snapshot.SelectedIndex, 0, Math.Max(0, documents.Length - 1)),
                snapshot.Revision);
            var current = LoadManifest();
            var windows = current.Windows
                .Where(candidate => candidate.Id != window.Id)
                .Append(window)
                .ToArray();
            SaveManifest(new NotepadSessionState(windows));
            _latestRevisions[snapshot.Id] = snapshot.Revision;
            DeleteUnreferencedPayloads(windows);
        }
    }

    public void RemoveWindow(Guid windowId, long revision)
    {
        lock (_sync)
        {
            if (IsStale(windowId, revision))
            {
                return;
            }

            var current = LoadManifest();
            var windows = current.Windows
                .Where(window => window.Id != windowId)
                .ToArray();
            if (windows.Length != current.Windows.Length)
            {
                SaveManifest(new NotepadSessionState(windows));
                DeleteUnreferencedPayloads(windows);
            }

            _latestRevisions[windowId] = revision;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            if (Directory.Exists(_documentsDirectory))
            {
                Directory.Delete(_documentsDirectory, recursive: true);
            }

            File.Delete(_manifestPath);
            _latestRevisions.Clear();
        }
    }

    private NotepadSessionDocumentState PersistDocument(
        Guid windowId,
        long revision,
        NotepadSessionDocumentSnapshot snapshot)
    {
        var state = snapshot.State;
        if (snapshot.ModifiedContent is null)
        {
            return state with
            {
                ModifiedText = null,
                RecoveryContentFile = null,
                HasRecoveryContent = false,
            };
        }

        var id = state.Id == Guid.Empty ? Guid.NewGuid() : state.Id;
        var fileName = $"{windowId:N}-{id:N}-{revision}.txt";
        var destination = Path.Combine(_documentsDirectory, fileName);
        WritePayloadAtomically(destination, snapshot.ModifiedContent);
        return state with
        {
            Id = id,
            ModifiedText = null,
            RecoveryContentFile = fileName,
            HasRecoveryContent = true,
        };
    }

    private NotepadSessionDocumentState HydrateDocument(NotepadSessionDocumentState state)
    {
        if (!state.HasRecoveryContent || string.IsNullOrWhiteSpace(state.RecoveryContentFile))
        {
            return state;
        }

        var fileName = Path.GetFileName(state.RecoveryContentFile);
        if (!string.Equals(fileName, state.RecoveryContentFile, StringComparison.Ordinal))
        {
            return state;
        }

        var path = Path.Combine(_documentsDirectory, fileName);
        return File.Exists(path)
            ? state with { ModifiedText = File.ReadAllText(path, Encoding.UTF8) }
            : state;
    }

    private NotepadSessionState LoadManifest()
    {
        if (!File.Exists(_manifestPath))
        {
            return NotepadSessionState.Empty;
        }

        var session = JsonSerializer.Deserialize<NotepadSessionState>(
            File.ReadAllText(_manifestPath, Encoding.UTF8));
        return session is null
            ? NotepadSessionState.Empty
            : new NotepadSessionState(session.Windows ?? []);
    }

    private void SaveManifest(NotepadSessionState session)
    {
        Directory.CreateDirectory(_directory);
        var temporaryPath = $"{_manifestPath}.tmp-{Guid.NewGuid():N}";
        try
        {
            WriteTextAtomically(
                temporaryPath,
                _manifestPath,
                JsonSerializer.Serialize(session));
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static void WritePayloadAtomically(string destination, ITextSnapshot content)
    {
        var temporaryPath = $"{destination}.tmp-{Guid.NewGuid():N}";
        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan))
            using (var writer = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 64 * 1024,
                leaveOpen: true))
            {
                content.WriteTo(writer);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, destination, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static void WriteTextAtomically(
        string temporaryPath,
        string destination,
        string text)
    {
        using (var stream = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 16 * 1024,
            FileOptions.SequentialScan))
        using (var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 16 * 1024,
            leaveOpen: true))
        {
            writer.Write(text);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, destination, overwrite: true);
    }

    private void DeleteUnreferencedPayloads(NotepadSessionWindowState[] windows)
    {
        if (!Directory.Exists(_documentsDirectory))
        {
            return;
        }

        var referenced = windows
            .SelectMany(window => window.Documents ?? [])
            .Where(document => document.HasRecoveryContent
                && !string.IsNullOrWhiteSpace(document.RecoveryContentFile))
            .Select(document => Path.GetFileName(document.RecoveryContentFile!))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(_documentsDirectory))
        {
            var fileName = Path.GetFileName(path);
            if (!referenced.Contains(fileName))
            {
                File.Delete(path);
            }
        }
    }

    private bool IsStale(Guid windowId, long revision)
    {
        if (_latestRevisions.TryGetValue(windowId, out var latest)
            && revision <= latest)
        {
            return true;
        }

        var persisted = LoadManifest().Windows
            .FirstOrDefault(window => window.Id == windowId);
        return persisted is not null && revision < persisted.Revision;
    }

}
