using System.Text.Json;
using Redmond.Avalonia.Windowing;

namespace Redmond.Notepad.Avalonia;

internal static class NotepadSettingsStore
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RedmondNotepad");
    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

    public static WindowAppearanceOptions LoadAppearance()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<WindowAppearanceOptions>(File.ReadAllText(SettingsPath))
                    ?? new WindowAppearanceOptions()
                : new WindowAppearanceOptions();
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
}
