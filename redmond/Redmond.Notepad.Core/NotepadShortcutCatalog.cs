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
}

public static class NotepadShortcutCatalog
{
    public static IReadOnlyList<ShortcutDefinition> CreateDefinitions() =>
    [
        Create(NotepadShortcutIds.NewTab, "N", ShortcutModifiers.Control, "T", ShortcutModifiers.Command),
        Create(NotepadShortcutIds.NewWindow, "N", ShortcutModifiers.Control | ShortcutModifiers.Shift,
            "N", ShortcutModifiers.Command),
        Create(NotepadShortcutIds.Open, "O", ShortcutModifiers.Control, "O", ShortcutModifiers.Command),
        Create(NotepadShortcutIds.Save, "S", ShortcutModifiers.Control, "S", ShortcutModifiers.Command),
        Create(NotepadShortcutIds.SaveAs, "S", ShortcutModifiers.Control | ShortcutModifiers.Shift,
            "S", ShortcutModifiers.Command | ShortcutModifiers.Shift),
        Create(NotepadShortcutIds.SaveAll, "S", ShortcutModifiers.Control | ShortcutModifiers.Alt,
            "S", ShortcutModifiers.Command | ShortcutModifiers.Alt),
        Create(NotepadShortcutIds.Print, "P", ShortcutModifiers.Control, "P", ShortcutModifiers.Command),
        Create(NotepadShortcutIds.CloseTab, "W", ShortcutModifiers.Control, "W", ShortcutModifiers.Command),
        Create(NotepadShortcutIds.CloseWindow, "W", ShortcutModifiers.Control | ShortcutModifiers.Shift,
            "W", ShortcutModifiers.Command | ShortcutModifiers.Shift),
    ];

    private static ShortcutDefinition Create(
        string id,
        string defaultKey,
        ShortcutModifiers defaultModifiers,
        string macOSKey,
        ShortcutModifiers macOSModifiers) =>
        new(
            id,
            new ShortcutGesture(ShortcutKey.Named(defaultKey), defaultModifiers),
            new Dictionary<ShortcutPlatform, ShortcutGesture>
            {
                [ShortcutPlatform.MacOS] = new(ShortcutKey.Named(macOSKey), macOSModifiers),
            });
}
