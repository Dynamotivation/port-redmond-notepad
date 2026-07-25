using Redmond.Notepad.Core;

namespace Redmond.Notepad.Avalonia;

internal sealed record NotepadSessionState(NotepadSessionWindowState[] Windows)
{
    public static NotepadSessionState Empty { get; } = new([]);
}

internal sealed record NotepadSessionWindowState(
    Guid Id,
    NotepadSessionDocumentState[] Documents,
    int SelectedIndex);

internal sealed record NotepadSessionDocumentState(
    string? Path,
    string? ModifiedText,
    string? EncodingName = null,
    bool HasByteOrderMark = false,
    LineEnding LineEnding = LineEnding.Crlf,
    FileContentVersion? BaseFileVersion = null);
