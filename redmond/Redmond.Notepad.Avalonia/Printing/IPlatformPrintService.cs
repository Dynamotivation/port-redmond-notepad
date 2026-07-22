using Redmond.Notepad.Core;

namespace Redmond.Notepad.Avalonia.Printing;

internal enum PlatformPrintResult
{
    Accepted,
    Cancelled,
    Unavailable,
    Failed,
}

internal interface IPlatformPrintService : IDisposable
{
    string? LastError { get; }

    PlatformPrintResult ShowPageSetup(IntPtr ownerWindow, ref NotepadPageSettings settings);

    PlatformPrintResult Print(IntPtr ownerWindow, NotepadPrintDocument document);
}

internal static class PlatformPrintServiceFactory
{
    public static IPlatformPrintService Create()
    {
        if (OperatingSystem.IsMacOS())
        {
            return new MacOSPrintService();
        }

        if (OperatingSystem.IsWindows())
        {
            return new WindowsPrintService();
        }

        return new UnsupportedPrintService();
    }
}

internal sealed class UnsupportedPrintService : IPlatformPrintService
{
    public string? LastError => "Native page setup and printing are not available on this platform yet.";

    public PlatformPrintResult ShowPageSetup(IntPtr ownerWindow, ref NotepadPageSettings settings) =>
        PlatformPrintResult.Unavailable;

    public PlatformPrintResult Print(IntPtr ownerWindow, NotepadPrintDocument document) =>
        PlatformPrintResult.Unavailable;

    public void Dispose()
    {
    }
}
