using System.Globalization;
using System.Text;

namespace Redmond.Notepad.Core;

public enum SpellingCorrectionAction
{
    GetSuggestions,
    Replace,
    Delete,
}

public enum AutocorrectMode
{
    Off,
    SafeReplacements,
    PlatformNative,
}

public sealed record SpellingIssue(
    int Start,
    int Length,
    SpellingCorrectionAction Action = SpellingCorrectionAction.GetSuggestions,
    string? Replacement = null)
{
    public int End => Start + Length;
}

public sealed record SpellCheckLanguage(string Tag, string DisplayName);

public interface ISpellCheckService : IDisposable
{
    bool IsAvailable { get; }

    string? ActiveLanguage { get; }

    IReadOnlyList<SpellCheckLanguage> AvailableLanguages { get; }

    IReadOnlyList<SpellingIssue> Check(string text);

    IReadOnlyList<string> Suggest(string word);

    string? GetAutomaticCorrection(string word, AutocorrectMode mode);

    void Ignore(string word);

    void AddToDictionary(string word);
}

public static class SpellCheckLanguageSelection
{
    public static string? SelectBest(
        IEnumerable<string> availableLanguages,
        CultureInfo? preferredCulture = null)
    {
        var available = availableLanguages
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (available.Length == 0)
        {
            return null;
        }

        preferredCulture ??= CultureInfo.CurrentCulture;
        static string NormalizeTag(string tag) => tag.Replace('_', '-');
        var exact = available.FirstOrDefault(
            tag => string.Equals(
                NormalizeTag(tag),
                preferredCulture.Name,
                StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var neutralName = preferredCulture.TwoLetterISOLanguageName;
        return available.FirstOrDefault(tag =>
                   tag.StartsWith(neutralName + "-", StringComparison.OrdinalIgnoreCase)
                   || tag.StartsWith(neutralName + "_", StringComparison.OrdinalIgnoreCase))
               ?? available[0];
    }
}

public static class AutocorrectLogic
{
    public static string? SelectCommonTypingCorrection(
        string word,
        IEnumerable<string> suggestions)
    {
        ArgumentNullException.ThrowIfNull(word);
        ArgumentNullException.ThrowIfNull(suggestions);
        return suggestions.FirstOrDefault(suggestion =>
            IsSingleAdjacentTransposition(word, suggestion));
    }

    public static bool IsSingleAdjacentTransposition(string original, string correction)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(correction);
        if (original.Length != correction.Length || original.Length < 2)
        {
            return false;
        }

        var firstDifference = -1;
        for (var index = 0; index < original.Length; index++)
        {
            if (char.ToUpperInvariant(original[index]) == char.ToUpperInvariant(correction[index]))
            {
                continue;
            }

            if (firstDifference >= 0)
            {
                return false;
            }

            firstDifference = index;
            if (index + 1 >= original.Length
                || char.ToUpperInvariant(original[index])
                    != char.ToUpperInvariant(correction[index + 1])
                || char.ToUpperInvariant(original[index + 1])
                    != char.ToUpperInvariant(correction[index]))
            {
                return false;
            }

            index++;
        }

        return firstDifference >= 0;
    }
}

public static class SpellCheckWordTokenizer
{
    public static IEnumerable<(int Start, int Length)> EnumerateWords(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var wordStart = -1;
        var index = 0;
        while (index < text.Length)
        {
            var rune = Rune.GetRuneAt(text, index);
            var runeLength = rune.Utf16SequenceLength;
            var isWordCharacter = IsWordCharacter(rune);
            var isInnerApostrophe = rune.Value is '\'' or 0x2019
                && wordStart >= 0
                && index + runeLength < text.Length
                && IsWordCharacter(Rune.GetRuneAt(text, index + runeLength));

            if (isWordCharacter || isInnerApostrophe)
            {
                wordStart = wordStart < 0 ? index : wordStart;
            }
            else if (wordStart >= 0)
            {
                yield return (wordStart, index - wordStart);
                wordStart = -1;
            }

            index += runeLength;
        }

        if (wordStart >= 0)
        {
            yield return (wordStart, text.Length - wordStart);
        }
    }

    private static bool IsWordCharacter(Rune rune) =>
        Rune.GetUnicodeCategory(rune) is UnicodeCategory.UppercaseLetter
            or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter
            or UnicodeCategory.OtherLetter
            or UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark;
}
