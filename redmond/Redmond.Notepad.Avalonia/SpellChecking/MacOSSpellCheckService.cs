using System.Globalization;
using System.Runtime.InteropServices;
using Redmond.Notepad.Core;

namespace Redmond.Notepad.Avalonia.SpellChecking;

internal sealed class MacOSSpellCheckService : ISpellCheckService
{
    private static readonly IntPtr AppKitHandle =
        NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");

    private readonly object _gate = new();
    private readonly IntPtr _checker;
    private readonly nint _documentTag;

    public MacOSSpellCheckService()
    {
        GC.KeepAlive(AppKitHandle);
        _checker = SendIntPtr(GetClass("NSSpellChecker"), Selector("sharedSpellChecker"));
        if (_checker == IntPtr.Zero)
        {
            AvailableLanguages = [];
            return;
        }

        SendVoidBool(_checker, Selector("setAutomaticallyIdentifiesLanguages:"), true);
        _documentTag = SendNInt(GetClass("NSSpellChecker"), Selector("uniqueSpellDocumentTag"));
        var tags = ReadStringArray(SendIntPtr(_checker, Selector("availableLanguages")));
        AvailableLanguages = tags
            .Select(tag => new SpellCheckLanguage(tag, GetLanguageDisplayName(tag)))
            .ToArray();
        ActiveLanguage = "Automatic";
    }

    public bool IsAvailable => _checker != IntPtr.Zero && AvailableLanguages.Count > 0;

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
            var nativeText = CreateString(text);
            try
            {
                var result = new List<SpellingIssue>();
                var startingAt = 0;
                while (startingAt < text.Length)
                {
                    var range = SendRangeCheck(
                        _checker,
                        Selector("checkSpellingOfString:startingAt:language:wrap:inSpellDocumentWithTag:wordCount:"),
                        nativeText,
                        startingAt,
                        IntPtr.Zero,
                        false,
                        _documentTag,
                        IntPtr.Zero);
                    if (range.Location < 0 || range.Length <= 0 || range.Location >= text.Length)
                    {
                        break;
                    }

                    var start = checked((int)range.Location);
                    var length = Math.Min(checked((int)range.Length), text.Length - start);
                    result.Add(new SpellingIssue(start, length));
                    startingAt = Math.Max(startingAt + 1, start + length);
                }

                return result;
            }
            finally
            {
                CFRelease(nativeText);
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
            var nativeWord = CreateString(word);
            try
            {
                var suggestions = SendIntPtrRangeIntPtrIntPtrNInt(
                    _checker,
                    Selector("guessesForWordRange:inString:language:inSpellDocumentWithTag:"),
                    new NativeRange(0, word.Length),
                    nativeWord,
                    IntPtr.Zero,
                    _documentTag);
                return ReadStringArray(suggestions).Take(8).ToArray();
            }
            finally
            {
                CFRelease(nativeWord);
            }
        }
    }

    public string? GetAutomaticCorrection(string word, AutocorrectMode mode)
    {
        if (mode == AutocorrectMode.Off || !IsAvailable || string.IsNullOrWhiteSpace(word))
        {
            return null;
        }

        if (mode == AutocorrectMode.SafeReplacements)
        {
            return AutocorrectLogic.SelectCommonTypingCorrection(word, Suggest(word));
        }

        lock (_gate)
        {
            var nativeWord = CreateString(word);
            try
            {
                var correction = SendIntPtrRangeIntPtrIntPtrNInt(
                    _checker,
                    Selector("correctionForWordRange:inString:language:inSpellDocumentWithTag:"),
                    new NativeRange(0, word.Length),
                    nativeWord,
                    IntPtr.Zero,
                    _documentTag);
                var value = GetString(correction);
                return string.IsNullOrWhiteSpace(value)
                    || string.Equals(value, word, StringComparison.CurrentCulture)
                        ? null
                        : value;
            }
            finally
            {
                CFRelease(nativeWord);
            }
        }
    }

    public void Ignore(string word)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(word))
        {
            return;
        }

        lock (_gate)
        {
            var nativeWord = CreateString(word);
            try
            {
                SendVoidIntPtrNInt(
                    _checker,
                    Selector("ignoreWord:inSpellDocumentWithTag:"),
                    nativeWord,
                    _documentTag);
            }
            finally
            {
                CFRelease(nativeWord);
            }
        }
    }

    public void AddToDictionary(string word)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(word))
        {
            return;
        }

        lock (_gate)
        {
            var nativeWord = CreateString(word);
            try
            {
                SendVoidIntPtr(_checker, Selector("learnWord:"), nativeWord);
            }
            finally
            {
                CFRelease(nativeWord);
            }
        }
    }

    public void Dispose()
    {
        if (_checker != IntPtr.Zero && _documentTag != 0)
        {
            SendVoidNInt(_checker, Selector("closeSpellDocumentWithTag:"), _documentTag);
        }
    }

    private static IReadOnlyList<string> ReadStringArray(IntPtr array)
    {
        if (array == IntPtr.Zero)
        {
            return [];
        }

        var count = checked((int)SendNUInt(array, Selector("count")));
        var result = new List<string>(count);
        for (var index = 0; index < count; index++)
        {
            var value = SendIntPtrNUInt(array, Selector("objectAtIndex:"), checked((nuint)index));
            var managed = GetString(value);
            if (!string.IsNullOrWhiteSpace(managed))
            {
                result.Add(managed);
            }
        }

        return result;
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

    private static IntPtr CreateString(string value)
    {
        var characters = Marshal.StringToCoTaskMemUni(value);
        try
        {
            return CFStringCreateWithCharacters(IntPtr.Zero, characters, value.Length);
        }
        finally
        {
            Marshal.FreeCoTaskMem(characters);
        }
    }

    private static string GetString(IntPtr value)
    {
        if (value == IntPtr.Zero)
        {
            return string.Empty;
        }

        var length = checked((int)CFStringGetLength(value));
        if (length == 0)
        {
            return string.Empty;
        }

        var characters = Marshal.AllocCoTaskMem(checked(length * sizeof(char)));
        try
        {
            CFStringGetCharacters(value, new NativeRange(0, length), characters);
            return Marshal.PtrToStringUni(characters, length) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeCoTaskMem(characters);
        }
    }

    private static IntPtr GetClass(string name) => objc_getClass(name);

    private static IntPtr Selector(string name) => sel_registerName(name);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeRange(nint Location, nint Length);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFStringCreateWithCharacters(IntPtr allocator, IntPtr characters, nint length);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern nint CFStringGetLength(IntPtr value);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFStringGetCharacters(IntPtr value, NativeRange range, IntPtr buffer);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendIntPtr(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint SendNInt(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nuint SendNUInt(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendIntPtrNUInt(IntPtr receiver, IntPtr selector, nuint index);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendVoidBool(
        IntPtr receiver,
        IntPtr selector,
        [MarshalAs(UnmanagedType.I1)] bool value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendVoidIntPtr(IntPtr receiver, IntPtr selector, IntPtr value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendVoidNInt(IntPtr receiver, IntPtr selector, nint value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendVoidIntPtrNInt(
        IntPtr receiver,
        IntPtr selector,
        IntPtr value,
        nint tag);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern NativeRange SendRangeCheck(
        IntPtr receiver,
        IntPtr selector,
        IntPtr text,
        nint startingAt,
        IntPtr language,
        [MarshalAs(UnmanagedType.I1)] bool wrap,
        nint documentTag,
        IntPtr wordCount);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendIntPtrRangeIntPtrIntPtrNInt(
        IntPtr receiver,
        IntPtr selector,
        NativeRange range,
        IntPtr text,
        IntPtr language,
        nint documentTag);
}
