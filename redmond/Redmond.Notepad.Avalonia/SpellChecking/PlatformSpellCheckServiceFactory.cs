using Redmond.Notepad.Core;

namespace Redmond.Notepad.Avalonia.SpellChecking;

internal static class PlatformSpellCheckServiceFactory
{
    public static ISpellCheckService Create()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return new WindowsSpellCheckService();
            }

            if (OperatingSystem.IsMacOS())
            {
                return new MacOSSpellCheckService();
            }

            if (OperatingSystem.IsLinux())
            {
                return new EnchantSpellCheckService();
            }
        }
        catch
        {
            // Missing language packs or a missing Linux Enchant installation
            // disable spell checking without preventing the editor from booting.
        }

        return UnavailableSpellCheckService.Instance;
    }
}

internal sealed class UnavailableSpellCheckService : ISpellCheckService
{
    public static UnavailableSpellCheckService Instance { get; } = new();

    public bool IsAvailable => false;

    public string? ActiveLanguage => null;

    public IReadOnlyList<SpellCheckLanguage> AvailableLanguages => [];

    public IReadOnlyList<SpellingIssue> Check(string text) => [];

    public IReadOnlyList<string> Suggest(string word) => [];

    public string? GetAutomaticCorrection(string word, AutocorrectMode mode) => null;

    public void Ignore(string word)
    {
    }

    public void AddToDictionary(string word)
    {
    }

    public void Dispose()
    {
    }
}
