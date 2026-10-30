using Redmond.Shortcuts;

namespace Redmond.Notepad.Core;

public static class NotepadShortcutIds
{
    public const string NewTab = "file.new-tab";
    public const string NewWindow = "file.new-window";
    public const string Open = "file.open";
    public const string Save = "file.save";
    public const string SaveAs = "file.save-as";
    public const string SaveAll = "file.save-all";
    public const string Print = "file.print";
    public const string CloseTab = "file.close-tab";
    public const string CloseWindow = "file.close-window";
    public const string Undo = "edit.undo";
    public const string Cut = "edit.cut";
    public const string Copy = "edit.copy";
    public const string Paste = "edit.paste";
    public const string Delete = "edit.delete";
    public const string Find = "edit.find";
    public const string FindNext = "edit.find-next";
    public const string FindPrevious = "edit.find-previous";
    public const string Replace = "edit.replace";
    public const string GoTo = "edit.go-to";
    public const string SelectAll = "edit.select-all";
    public const string TimeDate = "edit.time-date";
    public const string ZoomIn = "view.zoom-in";
    public const string ZoomOut = "view.zoom-out";
    public const string RestoreDefaultZoom = "view.restore-default-zoom";
}

/// <summary>
/// Every Notepad shortcut, defined once with the Windows Notepad gesture. Primary is
/// Ctrl on Windows and Linux and Command (or Control, by user choice) on macOS;
/// <see cref="ShortcutPrimaryModifier"/> in Redmond.Shortcuts resolves it.
/// </summary>
public static class NotepadShortcutCatalog
{
    public static IReadOnlyList<ShortcutDefinition> CreateDefinitions() =>
    [
        Create(NotepadShortcutIds.NewTab, "Primary+N"),
        Create(NotepadShortcutIds.NewWindow, "Primary+Shift+N"),
        Create(NotepadShortcutIds.Open, "Primary+O"),
        Create(NotepadShortcutIds.Save, "Primary+S"),
        Create(NotepadShortcutIds.SaveAs, "Primary+Shift+S"),
        Create(NotepadShortcutIds.SaveAll, "Primary+Alt+S"),
        Create(NotepadShortcutIds.Print, "Primary+P"),
        Create(NotepadShortcutIds.CloseTab, "Primary+W"),
        Create(NotepadShortcutIds.CloseWindow, "Primary+Shift+W"),
        Create(NotepadShortcutIds.Undo, "Primary+Z"),
        Create(NotepadShortcutIds.Cut, "Primary+X"),
        Create(NotepadShortcutIds.Copy, "Primary+C"),
        Create(NotepadShortcutIds.Paste, "Primary+V"),
        Create(NotepadShortcutIds.Delete, "Delete"),
        Create(NotepadShortcutIds.Find, "Primary+F"),
        Create(NotepadShortcutIds.FindNext, "F3"),
        Create(NotepadShortcutIds.FindPrevious, "Shift+F3"),
        Create(NotepadShortcutIds.Replace, "Primary+H"),
        Create(NotepadShortcutIds.GoTo, "Primary+G"),
        Create(NotepadShortcutIds.SelectAll, "Primary+A"),
        Create(NotepadShortcutIds.TimeDate, "F5"),
        Create(NotepadShortcutIds.ZoomIn, "Primary+OemPlus", "Primary+Shift+OemPlus", "Primary+NumPadAdd"),
        Create(NotepadShortcutIds.ZoomOut, "Primary+OemMinus", "Primary+NumPadSubtract"),
        Create(NotepadShortcutIds.RestoreDefaultZoom, "Primary+D0"),
    ];

    private static ShortcutDefinition Create(string id, string gesture, params string[] alternates) =>
        new(
            id,
            ShortcutGesture.Parse(gesture),
            alternateGestures: alternates.Select(ShortcutGesture.Parse).ToArray());
}
