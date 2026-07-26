using System.Diagnostics;
using System.Reflection;

namespace Redmond.Notepad.Avalonia;

internal static class ProcessLaunchService
{
    public const string NewWindowArgument = "--new-window";

    public static bool TryStartNewWindow(
        IEnumerable<string>? paths,
        out string? error)
    {
        error = null;
        try
        {
            var processPath = Environment.ProcessPath
                ?? throw new InvalidOperationException("The current executable path is unavailable.");
            var startInfo = new ProcessStartInfo
            {
                FileName = processPath,
                UseShellExecute = false,
            };
            if (string.Equals(
                Path.GetFileNameWithoutExtension(processPath),
                "dotnet",
                StringComparison.OrdinalIgnoreCase))
            {
                var assemblyPath = Assembly.GetEntryAssembly()?.Location;
                if (string.IsNullOrWhiteSpace(assemblyPath))
                {
                    throw new InvalidOperationException("The application assembly path is unavailable.");
                }

                startInfo.ArgumentList.Add(assemblyPath);
            }

            startInfo.ArgumentList.Add(NewWindowArgument);
            foreach (var path in paths ?? [])
            {
                startInfo.ArgumentList.Add(path);
            }

            Process.Start(startInfo);
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.ComponentModel.Win32Exception
            or IOException
            or UnauthorizedAccessException)
        {
            error = exception.Message;
            return false;
        }
    }
}
