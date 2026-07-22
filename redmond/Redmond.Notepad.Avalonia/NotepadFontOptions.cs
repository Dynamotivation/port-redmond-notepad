namespace Redmond.Notepad.Avalonia;

internal enum NotepadEditorFontStyle
{
    Regular,
    Italic,
    Bold,
    BoldItalic,
}

internal sealed record NotepadFontOptions(
    string Family,
    NotepadEditorFontStyle Style,
    double Size)
{
    public static NotepadFontOptions Default { get; } = new(
        OperatingSystem.IsWindows()
            ? "Consolas"
            : OperatingSystem.IsMacOS()
                ? "Menlo"
                : "monospace",
        NotepadEditorFontStyle.Regular,
        OperatingSystem.IsWindows() ? 11 : 13);

    public NotepadFontOptions Normalized() => this with
    {
        Family = string.IsNullOrWhiteSpace(Family) ? Default.Family : Family.Trim(),
        Style = Enum.IsDefined(Style) ? Style : NotepadEditorFontStyle.Regular,
        Size = Math.Clamp(Size, 8, 72),
    };
}
