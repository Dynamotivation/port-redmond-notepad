namespace Redmond.Notepad.Avalonia;

internal sealed record NotepadViewOptions(
    double ZoomPercent = 100,
    bool ShowStatusBar = true,
    bool WordWrap = true)
{
    public NotepadViewOptions Normalized() => this with
    {
        ZoomPercent = Math.Clamp(Math.Round(ZoomPercent / 10) * 10, 10, 500),
    };
}
