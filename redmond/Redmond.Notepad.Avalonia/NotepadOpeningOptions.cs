namespace Redmond.Notepad.Avalonia;

internal enum NotepadFileOpeningMode
{
    NewTab,
    NewWindow,
}

internal enum NotepadStartupMode
{
    NewWindow,
    ContinuePreviousSession,
}

internal sealed record NotepadOpeningOptions(
    NotepadFileOpeningMode FileOpeningMode = NotepadFileOpeningMode.NewTab,
    NotepadStartupMode StartupMode = NotepadStartupMode.ContinuePreviousSession)
{
    public NotepadOpeningOptions Normalized() => this with
    {
        FileOpeningMode = Enum.IsDefined(FileOpeningMode)
            ? FileOpeningMode
            : NotepadFileOpeningMode.NewTab,
        StartupMode = Enum.IsDefined(StartupMode)
            ? StartupMode
            : NotepadStartupMode.NewWindow,
    };
}
