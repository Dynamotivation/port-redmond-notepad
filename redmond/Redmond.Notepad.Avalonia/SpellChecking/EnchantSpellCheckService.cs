using System.Globalization;
using System.Runtime.InteropServices;
using Redmond.Notepad.Core;

namespace Redmond.Notepad.Avalonia.SpellChecking;

internal sealed class EnchantSpellCheckService : ISpellCheckService
{
    private static readonly string[] LibraryNames =
    [
        "libenchant-2.so.2",
        "libenchant-2.so",
        "enchant-2",
    ];

    private readonly object _gate = new();
    private readonly EnchantNative _native;
    private IntPtr _broker;
    private IntPtr _dictionary;

    public EnchantSpellCheckService()
    {
        _native = EnchantNative.Load(LibraryNames);
        _broker = _native.BrokerInit();
        if (_broker == IntPtr.Zero)
        {
            AvailableLanguages = [];
            return;
        }

        var tags = new List<string>();
        _native.BrokerListDictionaries(
            _broker,
            (languageTag, _, _, _, _) =>
            {
                var tag = Marshal.PtrToStringUTF8(languageTag);
                if (!string.IsNullOrWhiteSpace(tag))
                {
                    tags.Add(tag);
                }
            },
            IntPtr.Zero);

        var distinctTags = tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        AvailableLanguages = distinctTags
            .Select(tag => new SpellCheckLanguage(tag, GetLanguageDisplayName(tag)))
            .ToArray();
        ActiveLanguage = SpellCheckLanguageSelection.SelectBest(distinctTags);
        if (ActiveLanguage is not null)
        {
            _dictionary = _native.BrokerRequestDictionary(_broker, ActiveLanguage);
        }
    }

    public bool IsAvailable => _dictionary != IntPtr.Zero;

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
            var result = new List<SpellingIssue>();
            foreach (var (start, length) in SpellCheckWordTokenizer.EnumerateWords(text))
            {
                var word = text.Substring(start, length);
                if (_native.DictionaryCheck(_dictionary, word, -1) != 0)
                {
                    result.Add(new SpellingIssue(start, length));
                }
            }

            return result;
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
            var values = _native.DictionarySuggest(_dictionary, word, -1, out var count);
            if (values == IntPtr.Zero || count == 0)
            {
                return [];
            }

            try
            {
                var result = new List<string>(checked((int)Math.Min(count, 8)));
                for (nuint index = 0; index < count && index < 8; index++)
                {
                    var value = Marshal.ReadIntPtr(values, checked((int)index * IntPtr.Size));
                    var suggestion = Marshal.PtrToStringUTF8(value);
                    if (!string.IsNullOrWhiteSpace(suggestion))
                    {
                        result.Add(suggestion);
                    }
                }

                return result;
            }
            finally
            {
                _native.DictionaryFreeStringList(_dictionary, values);
            }
        }
    }

    public string? GetAutomaticCorrection(string word, AutocorrectMode mode)
    {
        if (mode == AutocorrectMode.Off)
        {
            return null;
        }

        var suggestions = Suggest(word);
        return mode == AutocorrectMode.PlatformNative
            ? suggestions.FirstOrDefault()
            : AutocorrectLogic.SelectCommonTypingCorrection(word, suggestions);
    }

    public void Ignore(string word)
    {
        if (IsAvailable && !string.IsNullOrWhiteSpace(word))
        {
            lock (_gate)
            {
                _native.DictionaryAddToSession(_dictionary, word, -1);
            }
        }
    }

    public void AddToDictionary(string word)
    {
        if (IsAvailable && !string.IsNullOrWhiteSpace(word))
        {
            lock (_gate)
            {
                _native.DictionaryAdd(_dictionary, word, -1);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_dictionary != IntPtr.Zero && _broker != IntPtr.Zero)
            {
                _native.BrokerFreeDictionary(_broker, _dictionary);
            }

            if (_broker != IntPtr.Zero)
            {
                _native.BrokerFree(_broker);
            }

            _dictionary = IntPtr.Zero;
            _broker = IntPtr.Zero;
            _native.Dispose();
        }
    }

    private static string GetLanguageDisplayName(string tag)
    {
        try
        {
            return CultureInfo.GetCultureInfo(tag.Replace('_', '-')).DisplayName;
        }
        catch (CultureNotFoundException)
        {
            return tag;
        }
    }

    private sealed class EnchantNative : IDisposable
    {
        private readonly IntPtr _library;

        private EnchantNative(IntPtr library)
        {
            _library = library;
            BrokerInit = Load<BrokerInitDelegate>("enchant_broker_init");
            BrokerFree = Load<BrokerFreeDelegate>("enchant_broker_free");
            BrokerListDictionaries = Load<BrokerListDictionariesDelegate>("enchant_broker_list_dicts");
            BrokerRequestDictionary = Load<BrokerRequestDictionaryDelegate>("enchant_broker_request_dict");
            BrokerFreeDictionary = Load<BrokerFreeDictionaryDelegate>("enchant_broker_free_dict");
            DictionaryCheck = Load<DictionaryCheckDelegate>("enchant_dict_check");
            DictionarySuggest = Load<DictionarySuggestDelegate>("enchant_dict_suggest");
            DictionaryFreeStringList = Load<DictionaryFreeStringListDelegate>("enchant_dict_free_string_list");
            DictionaryAddToSession = Load<DictionaryWordActionDelegate>("enchant_dict_add_to_session");
            DictionaryAdd = Load<DictionaryWordActionDelegate>("enchant_dict_add");
        }

        public BrokerInitDelegate BrokerInit { get; }
        public BrokerFreeDelegate BrokerFree { get; }
        public BrokerListDictionariesDelegate BrokerListDictionaries { get; }
        public BrokerRequestDictionaryDelegate BrokerRequestDictionary { get; }
        public BrokerFreeDictionaryDelegate BrokerFreeDictionary { get; }
        public DictionaryCheckDelegate DictionaryCheck { get; }
        public DictionarySuggestDelegate DictionarySuggest { get; }
        public DictionaryFreeStringListDelegate DictionaryFreeStringList { get; }
        public DictionaryWordActionDelegate DictionaryAddToSession { get; }
        public DictionaryWordActionDelegate DictionaryAdd { get; }

        public static EnchantNative Load(IEnumerable<string> names)
        {
            foreach (var name in names)
            {
                if (NativeLibrary.TryLoad(name, out var library))
                {
                    return new EnchantNative(library);
                }
            }

            throw new DllNotFoundException("Enchant 2 is not installed.");
        }

        public void Dispose() => NativeLibrary.Free(_library);

        private T Load<T>(string symbol) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, symbol));
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr BrokerInitDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void BrokerFreeDelegate(IntPtr broker);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void BrokerListDictionariesDelegate(
        IntPtr broker,
        DictionaryDescribeCallback callback,
        IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DictionaryDescribeCallback(
        IntPtr languageTag,
        IntPtr providerName,
        IntPtr providerDescription,
        IntPtr providerFile,
        IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr BrokerRequestDictionaryDelegate(
        IntPtr broker,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string languageTag);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void BrokerFreeDictionaryDelegate(IntPtr broker, IntPtr dictionary);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DictionaryCheckDelegate(
        IntPtr dictionary,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string word,
        nint length);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr DictionarySuggestDelegate(
        IntPtr dictionary,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string word,
        nint length,
        out nuint count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DictionaryFreeStringListDelegate(IntPtr dictionary, IntPtr values);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DictionaryWordActionDelegate(
        IntPtr dictionary,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string word,
        nint length);
}
