using Redmond.Notepad.Core;

namespace Redmond.Notepad.Avalonia;

internal sealed record NotepadSpellingOptions(
    bool Enabled = true,
    bool TxtEnabled = true,
    bool MarkdownEnabled = true,
    bool SubtitleEnabled = true,
    bool LyricsEnabled = true,
    bool LicenseEnabled = true,
    AutocorrectMode AutocorrectMode = AutocorrectMode.SafeReplacements)
{
    public NotepadSpellingOptions Normalized() => this with
    {
        AutocorrectMode = Enum.IsDefined(AutocorrectMode)
            ? AutocorrectMode
            : AutocorrectMode.Off,
    };

    public bool IsEnabledForPath(string? path)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(path))
        {
            return Enabled;
        }

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".txt" => TxtEnabled,
            ".md" or ".markdown" => MarkdownEnabled,
            ".srt" or ".ass" => SubtitleEnabled,
            ".lrc" => LyricsEnabled,
            ".lic" or ".license" => LicenseEnabled,
            _ => false,
        };
    }
}
