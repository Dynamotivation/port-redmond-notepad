using System.Globalization;
using System.Runtime.InteropServices;
using Redmond.Notepad.Core;

namespace Redmond.Notepad.Avalonia.SpellChecking;

internal sealed class WindowsSpellCheckService : ISpellCheckService
{
    private static readonly Guid SpellCheckerFactoryClassId =
        new("7AB36653-1796-484B-BDFA-E74F1DB7C1DC");
    private static readonly Guid SpellCheckerFactoryInterfaceId =
        new("8E018A9D-2415-4677-BF08-794EA61F94BB");

    private readonly object _gate = new();
    private IntPtr _factory;
    private IntPtr _checker;

    public WindowsSpellCheckService()
    {
        var classId = SpellCheckerFactoryClassId;
        var interfaceId = SpellCheckerFactoryInterfaceId;
        ThrowIfFailed(CoCreateInstance(
            ref classId,
            IntPtr.Zero,
            1,
            ref interfaceId,
            out _factory));

        var tags = EnumerateSupportedLanguages(_factory);
        AvailableLanguages = tags
            .Select(tag => new SpellCheckLanguage(tag, GetLanguageDisplayName(tag)))
            .ToArray();
        ActiveLanguage = SpellCheckLanguageSelection.SelectBest(tags);
        if (ActiveLanguage is null)
        {
            return;
        }

        var create = GetDelegate<CreateSpellCheckerDelegate>(_factory, 5);
        ThrowIfFailed(create(_factory, ActiveLanguage, out _checker));
    }

    public bool IsAvailable => _checker != IntPtr.Zero;

    public string? ActiveLanguage { get; }

    public IReadOnlyList<SpellCheckLanguage> AvailableLanguages { get; }

    public IReadOnlyList<SpellingIssue> Check(string text)
    {
        if (!IsAvailable || string.IsNullOrEmpty(text))
        {
            return [];
        }

        lock (_gate)
        {
            var check = GetDelegate<CheckDelegate>(_checker, 4);
            ThrowIfFailed(check(_checker, text, out var errors));
            if (errors == IntPtr.Zero)
            {
                return [];
            }

            try
            {
                var result = new List<SpellingIssue>();
                var next = GetDelegate<NextSpellingErrorDelegate>(errors, 3);
                while (next(errors, out var error) == 0 && error != IntPtr.Zero)
                {
                    try
                    {
                        var getStart = GetDelegate<GetUIntDelegate>(error, 3);
                        var getLength = GetDelegate<GetUIntDelegate>(error, 4);
                        var getAction = GetDelegate<GetIntDelegate>(error, 5);
                        ThrowIfFailed(getStart(error, out var start));
                        ThrowIfFailed(getLength(error, out var length));
                        ThrowIfFailed(getAction(error, out var action));

                        string? replacement = null;
                        if (action == 2)
                        {
                            var getReplacement = GetDelegate<GetStringDelegate>(error, 6);
                            ThrowIfFailed(getReplacement(error, out var replacementPointer));
                            replacement = ConsumeCoTaskMemString(replacementPointer);
                        }

                        result.Add(new SpellingIssue(
                            checked((int)start),
                            checked((int)length),
                            action switch
                            {
                                2 => SpellingCorrectionAction.Replace,
                                3 => SpellingCorrectionAction.Delete,
                                _ => SpellingCorrectionAction.GetSuggestions,
                            },
                            replacement));
                    }
                    finally
                    {
                        Release(error);
                    }
                }

                return result;
            }
            finally
            {
                Release(errors);
            }
        }
    }

    public IReadOnlyList<string> Suggest(string word)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(word))
        {
            return [];
        }

        lock (_gate)
        {
            var suggest = GetDelegate<SuggestDelegate>(_checker, 5);
            var resultCode = suggest(_checker, word, out var suggestions);
            if (resultCode < 0 || suggestions == IntPtr.Zero)
            {
                return [];
            }

            try
            {
                return EnumerateStrings(suggestions).Take(8).ToArray();
            }
            finally
            {
                Release(suggestions);
            }
        }
    }

    public string? GetAutomaticCorrection(string word, AutocorrectMode mode)
    {
        if (mode == AutocorrectMode.Off || string.IsNullOrWhiteSpace(word))
        {
            return null;
        }

        var replacement = Check(word).FirstOrDefault(issue =>
            issue.Start == 0
            && issue.Length == word.Length
            && issue.Action == SpellingCorrectionAction.Replace
            && !string.IsNullOrWhiteSpace(issue.Replacement));
        if (replacement?.Replacement is { } engineReplacement)
        {
            return engineReplacement;
        }

        var suggestions = Suggest(word);
        return mode == AutocorrectMode.PlatformNative
            ? suggestions.FirstOrDefault()
            : AutocorrectLogic.SelectCommonTypingCorrection(word, suggestions);
    }

    public void Ignore(string word) => InvokeWordAction(word, 7);

    public void AddToDictionary(string word) => InvokeWordAction(word, 6);

    public void Dispose()
    {
        lock (_gate)
        {
            Release(_checker);
            Release(_factory);
            _checker = IntPtr.Zero;
            _factory = IntPtr.Zero;
        }
    }

    private void InvokeWordAction(string word, int vtableIndex)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(word))
        {
            return;
        }

        lock (_gate)
        {
            var action = GetDelegate<WordActionDelegate>(_checker, vtableIndex);
            ThrowIfFailed(action(_checker, word));
        }
    }

    private static string[] EnumerateSupportedLanguages(IntPtr factory)
    {
        var getLanguages = GetDelegate<GetPointerDelegate>(factory, 3);
        ThrowIfFailed(getLanguages(factory, out var languages));
        if (languages == IntPtr.Zero)
        {
            return [];
        }

        try
        {
            return EnumerateStrings(languages).ToArray();
        }
        finally
        {
            Release(languages);
        }
    }

    private static IEnumerable<string> EnumerateStrings(IntPtr enumerator)
    {
        var next = GetDelegate<NextStringDelegate>(enumerator, 3);
        var itemBuffer = Marshal.AllocCoTaskMem(IntPtr.Size);
        try
        {
            while (true)
            {
                Marshal.WriteIntPtr(itemBuffer, IntPtr.Zero);
                var result = next(enumerator, 1, itemBuffer, out var fetched);
                if (result != 0 || fetched == 0)
                {
                    yield break;
                }

                var value = Marshal.ReadIntPtr(itemBuffer);
                yield return ConsumeCoTaskMemString(value) ?? string.Empty;
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(itemBuffer);
        }
    }

    private static string GetLanguageDisplayName(string tag)
    {
        try
        {
            return CultureInfo.GetCultureInfo(tag).DisplayName;
        }
        catch (CultureNotFoundException)
        {
            return tag;
        }
    }

    private static string? ConsumeCoTaskMemString(IntPtr value)
    {
        if (value == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(value);
        }
        finally
        {
            Marshal.FreeCoTaskMem(value);
        }
    }

    private static T GetDelegate<T>(IntPtr instance, int index) where T : Delegate
    {
        var vtable = Marshal.ReadIntPtr(instance);
        var function = Marshal.ReadIntPtr(vtable, index * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(function);
    }

    private static void Release(IntPtr instance)
    {
        if (instance == IntPtr.Zero)
        {
            return;
        }

        GetDelegate<ReleaseDelegate>(instance, 2)(instance);
    }

    private static void ThrowIfFailed(int result)
    {
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        ref Guid classId,
        IntPtr outer,
        uint context,
        ref Guid interfaceId,
        out IntPtr instance);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint ReleaseDelegate(IntPtr instance);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetPointerDelegate(IntPtr instance, out IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateSpellCheckerDelegate(
        IntPtr instance,
        [MarshalAs(UnmanagedType.LPWStr)] string languageTag,
        out IntPtr checker);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CheckDelegate(
        IntPtr instance,
        [MarshalAs(UnmanagedType.LPWStr)] string text,
        out IntPtr errors);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SuggestDelegate(
        IntPtr instance,
        [MarshalAs(UnmanagedType.LPWStr)] string word,
        out IntPtr suggestions);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int WordActionDelegate(
        IntPtr instance,
        [MarshalAs(UnmanagedType.LPWStr)] string word);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int NextSpellingErrorDelegate(IntPtr instance, out IntPtr error);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetUIntDelegate(IntPtr instance, out uint value);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetIntDelegate(IntPtr instance, out int value);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetStringDelegate(IntPtr instance, out IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int NextStringDelegate(
        IntPtr instance,
        uint count,
        IntPtr values,
        out uint fetched);
}
