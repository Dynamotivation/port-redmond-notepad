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

public static class NotepadShortcutCatalog
{
    public static IReadOnlyList<ShortcutDefinition> CreateDefinitions(
        MacOSShortcutModifier macOSModifier = MacOSShortcutModifier.Command) =>
        CreateCommandDefinitions()
            .Select(definition => MacOSShortcutModifierMapping.Apply(definition, macOSModifier))
            .ToArray();

    private static IReadOnlyList<ShortcutDefinition> CreateCommandDefinitions() =>
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
        Create(NotepadShortcutIds.Undo, "Z", ShortcutModifiers.Control, "Z", ShortcutModifiers.Command),
        Create(NotepadShortcutIds.Cut, "X", ShortcutModifiers.Control, "X", ShortcutModifiers.Command),
        Create(NotepadShortcutIds.Copy, "C", ShortcutModifiers.Control, "C", ShortcutModifiers.Command),
        Create(NotepadShortcutIds.Paste, "V", ShortcutModifiers.Control, "V", ShortcutModifiers.Command),
        Create(NotepadShortcutIds.Delete, "Delete", ShortcutModifiers.None, "Delete", ShortcutModifiers.None),
        Create(NotepadShortcutIds.Find, "F", ShortcutModifiers.Control, "F", ShortcutModifiers.Command),
        Create(NotepadShortcutIds.FindNext, "F3", ShortcutModifiers.None, "G", ShortcutModifiers.Command),
        Create(NotepadShortcutIds.FindPrevious, "F3", ShortcutModifiers.Shift,
            "G", ShortcutModifiers.Command | ShortcutModifiers.Shift),
        Create(NotepadShortcutIds.Replace, "H", ShortcutModifiers.Control,
            "F", ShortcutModifiers.Command | ShortcutModifiers.Alt),
        Create(NotepadShortcutIds.GoTo, "G", ShortcutModifiers.Control, "L", ShortcutModifiers.Command),
        Create(NotepadShortcutIds.SelectAll, "A", ShortcutModifiers.Control, "A", ShortcutModifiers.Command),
        Create(NotepadShortcutIds.TimeDate, "F5", ShortcutModifiers.None, "F5", ShortcutModifiers.None),
        new ShortcutDefinition(
            NotepadShortcutIds.ZoomIn,
            new ShortcutGesture(ShortcutKey.Named("OemPlus"), ShortcutModifiers.Control),
            new Dictionary<ShortcutPlatform, ShortcutGesture>
            {
                [ShortcutPlatform.MacOS] = new(ShortcutKey.Named("OemPlus"), ShortcutModifiers.Command),
            },
            alternateGestures:
            [
                new ShortcutGesture(
                    ShortcutKey.Named("OemPlus"),
                    ShortcutModifiers.Control | ShortcutModifiers.Shift),
                new ShortcutGesture(ShortcutKey.Named("NumPadAdd"), ShortcutModifiers.Control),
            ],
            platformAlternateGestures: new Dictionary<ShortcutPlatform, IReadOnlyList<ShortcutGesture>>
            {
                [ShortcutPlatform.MacOS] =
                [
                    new ShortcutGesture(
                        ShortcutKey.Named("OemPlus"),
                        ShortcutModifiers.Command | ShortcutModifiers.Shift),
                    new ShortcutGesture(ShortcutKey.Named("NumPadAdd"), ShortcutModifiers.Command),
                ],
            }),
        new ShortcutDefinition(
            NotepadShortcutIds.ZoomOut,
            new ShortcutGesture(ShortcutKey.Named("OemMinus"), ShortcutModifiers.Control),
            new Dictionary<ShortcutPlatform, ShortcutGesture>
            {
                [ShortcutPlatform.MacOS] = new(ShortcutKey.Named("OemMinus"), ShortcutModifiers.Command),
            },
            alternateGestures:
            [
                new ShortcutGesture(ShortcutKey.Named("NumPadSubtract"), ShortcutModifiers.Control),
            ],
            platformAlternateGestures: new Dictionary<ShortcutPlatform, IReadOnlyList<ShortcutGesture>>
            {
                [ShortcutPlatform.MacOS] =
                [
                    new ShortcutGesture(ShortcutKey.Named("NumPadSubtract"), ShortcutModifiers.Command),
                ],
            }),
        Create(
            NotepadShortcutIds.RestoreDefaultZoom,
            "D0",
            ShortcutModifiers.Control,
            "D0",
            ShortcutModifiers.Command),
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
