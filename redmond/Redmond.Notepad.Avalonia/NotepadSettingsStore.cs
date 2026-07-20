using System.Text.Json;
using Redmond.Avalonia.Controls;

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
}
