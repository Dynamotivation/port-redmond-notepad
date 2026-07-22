using System.Text.Json;
using Redmond.Avalonia.Controls;
using Redmond.Notepad.Core;

namespace Redmond.Notepad.Avalonia;

internal static class NotepadSettingsStore
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RedmondNotepad");
    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");
    private static readonly string ThemePath = Path.Combine(SettingsDirectory, "theme.txt");
    private static readonly string RecentFilesPath = Path.Combine(SettingsDirectory, "recent-files.json");
    private static readonly string RecentFilesEnabledPath = Path.Combine(SettingsDirectory, "recent-files-enabled.txt");
    private static readonly string ViewPath = Path.Combine(SettingsDirectory, "view.json");

    public static AppThemePreference LoadThemePreference()
    {
        try
        {
            return File.Exists(ThemePath)
                && Enum.TryParse<AppThemePreference>(File.ReadAllText(ThemePath), ignoreCase: true, out var preference)
                    ? preference
                    : AppThemePreference.System;
        }
        catch (IOException)
        {
            return AppThemePreference.System;
        }
        catch (UnauthorizedAccessException)
        {
            return AppThemePreference.System;
        }
    }

    public static void SaveThemePreference(AppThemePreference preference)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(ThemePath, preference.ToString());
        }
        catch (IOException)
        {
            // Theme remains live when persistence is unavailable.
        }
        catch (UnauthorizedAccessException)
        {
            // Theme remains live when persistence is unavailable.
        }
    }

    public static WindowAppearanceOptions LoadAppearance()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new WindowAppearanceOptions();
            }

            var json = File.ReadAllText(SettingsPath);
            var options = JsonSerializer.Deserialize<WindowAppearanceOptions>(json)
                ?? new WindowAppearanceOptions();
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(nameof(WindowAppearanceOptions.BackgroundMode), out _)
                && document.RootElement.TryGetProperty("UseSystemBackdrop", out var legacyBackdrop)
                && legacyBackdrop.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                options = options with
                {
                    BackgroundMode = legacyBackdrop.GetBoolean()
                        ? TranslucentBackgroundMode.WhenSelected
                        : TranslucentBackgroundMode.Never,
                };
            }

            return options;
        }
        catch (IOException)
        {
            return new WindowAppearanceOptions();
        }
        catch (JsonException)
        {
            return new WindowAppearanceOptions();
        }
        catch (UnauthorizedAccessException)
        {
            return new WindowAppearanceOptions();
        }
    }

    public static void SaveAppearance(WindowAppearanceOptions options)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var temporaryPath = SettingsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(options));
            File.Move(temporaryPath, SettingsPath, overwrite: true);
        }
        catch (IOException)
        {
            // Appearance remains live when persistence is unavailable.
        }
        catch (UnauthorizedAccessException)
        {
            // Appearance remains live when persistence is unavailable.
        }
    }

    public static bool LoadRecentFilesEnabled()
    {
        try
        {
            return !File.Exists(RecentFilesEnabledPath)
                || !bool.TryParse(File.ReadAllText(RecentFilesEnabledPath), out var enabled)
                || enabled;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    public static void SaveRecentFilesEnabled(bool enabled)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(RecentFilesEnabledPath, enabled.ToString());
        }
        catch (IOException)
        {
            // The setting remains live when persistence is unavailable.
        }
        catch (UnauthorizedAccessException)
        {
            // The setting remains live when persistence is unavailable.
        }
    }

    public static RecentFileList LoadRecentFiles()
    {
        try
        {
            if (!File.Exists(RecentFilesPath))
            {
                return new RecentFileList();
            }

            return new RecentFileList(
                JsonSerializer.Deserialize<string[]>(File.ReadAllText(RecentFilesPath)) ?? []);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or ArgumentException
            or NotSupportedException)
        {
            return new RecentFileList();
        }
    }

    public static RecentFileList RecordRecentFile(string path)
    {
        var recentFiles = LoadRecentFiles();
        recentFiles.Add(path);
        SaveRecentFiles(recentFiles);
        return recentFiles;
    }

    public static RecentFileList RemoveRecentFile(string path)
    {
        var recentFiles = LoadRecentFiles();
        if (recentFiles.Remove(path))
        {
            SaveRecentFiles(recentFiles);
        }

        return recentFiles;
    }

    public static void ClearRecentFiles() => SaveRecentFiles(new RecentFileList());

    private static void SaveRecentFiles(RecentFileList recentFiles)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var temporaryPath = RecentFilesPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(recentFiles.Paths));
            File.Move(temporaryPath, RecentFilesPath, overwrite: true);
        }
        catch (IOException)
        {
            // Recent files remain available for this session when persistence is unavailable.
        }
        catch (UnauthorizedAccessException)
        {
            // Recent files remain available for this session when persistence is unavailable.
        }
    }

    public static NotepadViewOptions LoadViewOptions()
    {
        try
        {
            return File.Exists(ViewPath)
                ? (JsonSerializer.Deserialize<NotepadViewOptions>(File.ReadAllText(ViewPath))
                    ?? new NotepadViewOptions()).Normalized()
                : new NotepadViewOptions();
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or NotSupportedException)
        {
            return new NotepadViewOptions();
        }
    }

    public static void SaveViewOptions(NotepadViewOptions options)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var temporaryPath = ViewPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(options.Normalized()));
            File.Move(temporaryPath, ViewPath, overwrite: true);
        }
        catch (IOException)
        {
            // View options remain live when persistence is unavailable.
        }
        catch (UnauthorizedAccessException)
        {
            // View options remain live when persistence is unavailable.
        }
    }
}
