using System.Text.Json;
using Redmond.Avalonia.Controls;
using Redmond.Notepad.Core;

namespace Redmond.Notepad.Avalonia;

internal static class NotepadSettingsStore
{
    private static readonly string SettingsDirectory =
        Environment.GetEnvironmentVariable("REDMOND_NOTEPAD_SETTINGS_DIRECTORY")
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RedmondNotepad");
    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");
    private static readonly string ThemePath = Path.Combine(SettingsDirectory, "theme.txt");
    private static readonly string RecentFilesPath = Path.Combine(SettingsDirectory, "recent-files.json");
    private static readonly string RecentFilesEnabledPath = Path.Combine(SettingsDirectory, "recent-files-enabled.txt");
    private static readonly string ViewPath = Path.Combine(SettingsDirectory, "view.json");
    private static readonly string FontPath = Path.Combine(SettingsDirectory, "editor-font.json");
    private static readonly string OpeningPath = Path.Combine(SettingsDirectory, "opening.json");
    private static readonly string NativeMacOSMenuBarPath = Path.Combine(SettingsDirectory, "native-macos-menu-bar.txt");
    private static readonly string MacOSShortcutModifierPath = Path.Combine(SettingsDirectory, "macos-shortcut-modifier.txt");
    private static readonly string SpellingPath = Path.Combine(SettingsDirectory, "spelling.json");
    private static readonly NotepadSessionRecoveryStore SessionStore = new(SettingsDirectory);

    public static event EventHandler<NotepadFontOptions>? FontOptionsChanged;

    public static event EventHandler<NotepadOpeningOptions>? OpeningOptionsChanged;

    public static event EventHandler<bool>? NativeMacOSMenuBarChanged;

    public static event EventHandler<MacOSShortcutModifier>? MacOSShortcutModifierChanged;

    public static event EventHandler<NotepadSpellingOptions>? SpellingOptionsChanged;

    public static NotepadProcessRegistration RegisterProcess() =>
        NotepadProcessRegistration.Register(SettingsDirectory);

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

    public static bool LoadUseNativeMacOSMenuBar()
    {
        try
        {
            return File.Exists(NativeMacOSMenuBarPath)
                && bool.TryParse(File.ReadAllText(NativeMacOSMenuBarPath), out var enabled)
                && enabled;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void SaveUseNativeMacOSMenuBar(bool enabled)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(NativeMacOSMenuBarPath, enabled.ToString());
        }
        catch (IOException)
        {
            // The setting remains live when persistence is unavailable.
        }
        catch (UnauthorizedAccessException)
        {
            // The setting remains live when persistence is unavailable.
        }

        NativeMacOSMenuBarChanged?.Invoke(null, enabled);
    }

    public static MacOSShortcutModifier LoadMacOSShortcutModifier()
    {
        try
        {
            return File.Exists(MacOSShortcutModifierPath)
                && Enum.TryParse<MacOSShortcutModifier>(
                    File.ReadAllText(MacOSShortcutModifierPath).Trim(),
                    ignoreCase: true,
                    out var modifier)
                && Enum.IsDefined(modifier)
                    ? modifier
                    : MacOSShortcutModifier.Command;
        }
        catch (IOException)
        {
            return MacOSShortcutModifier.Command;
        }
        catch (UnauthorizedAccessException)
        {
            return MacOSShortcutModifier.Command;
        }
    }

    public static void SaveMacOSShortcutModifier(MacOSShortcutModifier modifier)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(MacOSShortcutModifierPath, modifier.ToString());
        }
        catch (IOException)
        {
            // The setting remains live when persistence is unavailable.
        }
        catch (UnauthorizedAccessException)
        {
            // The setting remains live when persistence is unavailable.
        }

        MacOSShortcutModifierChanged?.Invoke(null, modifier);
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

    public static NotepadFontOptions LoadFontOptions()
    {
        try
        {
            return File.Exists(FontPath)
                ? (JsonSerializer.Deserialize<NotepadFontOptions>(File.ReadAllText(FontPath))
                    ?? NotepadFontOptions.Default).Normalized()
                : NotepadFontOptions.Default;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or NotSupportedException)
        {
            return NotepadFontOptions.Default;
        }
    }

    public static void SaveFontOptions(NotepadFontOptions options)
    {
        options = options.Normalized();
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var temporaryPath = FontPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(options));
            File.Move(temporaryPath, FontPath, overwrite: true);
        }
        catch (IOException)
        {
            // Font options remain live when persistence is unavailable.
        }
        catch (UnauthorizedAccessException)
        {
            // Font options remain live when persistence is unavailable.
        }

        FontOptionsChanged?.Invoke(null, options);
    }

    public static NotepadOpeningOptions LoadOpeningOptions()
    {
        try
        {
            return File.Exists(OpeningPath)
                ? (JsonSerializer.Deserialize<NotepadOpeningOptions>(File.ReadAllText(OpeningPath))
                    ?? new NotepadOpeningOptions()).Normalized()
                : new NotepadOpeningOptions();
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or NotSupportedException)
        {
            return new NotepadOpeningOptions();
        }
    }

    public static void SaveOpeningOptions(NotepadOpeningOptions options)
    {
        options = options.Normalized();
        TrySaveJson(OpeningPath, options);
        OpeningOptionsChanged?.Invoke(null, options);
    }

    public static NotepadSpellingOptions LoadSpellingOptions()
    {
        try
        {
            return File.Exists(SpellingPath)
                ? (JsonSerializer.Deserialize<NotepadSpellingOptions>(File.ReadAllText(SpellingPath))
                    ?? new NotepadSpellingOptions()).Normalized()
                : new NotepadSpellingOptions();
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or NotSupportedException)
        {
            return new NotepadSpellingOptions();
        }
    }

    public static void SaveSpellingOptions(NotepadSpellingOptions options)
    {
        options = options.Normalized();
        TrySaveJson(SpellingPath, options);
        SpellingOptionsChanged?.Invoke(null, options);
    }

    public static NotepadSessionState LoadSession()
    {
        try
        {
            return SessionStore.Load();
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or ArgumentException
            or NotSupportedException)
        {
            return NotepadSessionState.Empty;
        }
    }

    public static NotepadSessionWindowState? LoadMostRecentSessionWindow()
    {
        try
        {
            return SessionStore.LoadMostRecentWindow();
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or ArgumentException
            or NotSupportedException)
        {
            return null;
        }
    }

    public static void SaveSessionWindow(NotepadSessionWindowSnapshot snapshot)
    {
        try
        {
            SessionStore.SaveWindow(snapshot);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or ArgumentException
            or NotSupportedException)
        {
            // A later edit will retry the recovery snapshot.
        }
    }

    public static void RemoveSessionWindow(Guid windowId, long revision = long.MaxValue)
    {
        try
        {
            SessionStore.RemoveWindow(windowId, revision);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or ArgumentException
            or NotSupportedException)
        {
            // A stale session is preferable to disrupting shutdown.
        }
    }

    public static void ClearSession()
    {
        try
        {
            SessionStore.Clear();
        }
        catch (IOException)
        {
            // A stale session is preferable to disrupting shutdown.
        }
        catch (UnauthorizedAccessException)
        {
            // A stale session is preferable to disrupting shutdown.
        }
    }

    private static void TrySaveJson<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value));
            File.Move(temporaryPath, path, overwrite: true);
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
}
