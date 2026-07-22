using System.Globalization;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using Redmond.Notepad.Core;

namespace Redmond.Notepad.Avalonia.Printing;

/// <summary>
/// Bridges Avalonia's native NSWindow to AppKit's own page-layout and print
/// panels. This intentionally avoids reproducing either system surface in XAML.
/// </summary>
internal sealed class MacOSPrintService : IPlatformPrintService
{
    private const nint NSModalResponseOk = 1;
    private const double PointsPerMillimetre = 72.0 / 25.4;
    private const string PrintableTextViewClassName = "RedmondNotepadPrintableTextView";

    private IntPtr _printInfo;
    private static readonly ConcurrentDictionary<IntPtr, PageBorderContext> PageBorderContexts = new();
    private static readonly DrawPageBorderCallback DrawPageBorderHandler = DrawPageBorder;
    private static IntPtr _printableTextViewClass;

    public string? LastError { get; private set; }

    public PlatformPrintResult ShowPageSetup(IntPtr ownerWindow, ref NotepadPageSettings settings)
    {
        LastError = null;
        try
        {
            EnsurePrintInfo(settings);
            using var pool = new AutoreleasePool();
            var pageLayout = SendIntPtr(GetClass("NSPageLayout"), Selector("pageLayout"));
            var accessory = CreateAccessory(settings);
            SendVoidIntPtr(pageLayout, Selector("addAccessoryController:"), accessory.Controller);

            var response = SendNIntIntPtr(pageLayout, Selector("runModalWithPrintInfo:"), _printInfo);
            if (response != NSModalResponseOk)
            {
                Release(accessory.Controller);
                return PlatformPrintResult.Cancelled;
            }

            settings = settings with
            {
                PaperName = GetString(SendIntPtr(_printInfo, Selector("paperName"))),
                Landscape = SendNInt(_printInfo, Selector("orientation")) != 0,
                LeftMarginMillimetres = FromPoints(SendDouble(_printInfo, Selector("leftMargin"))),
                RightMarginMillimetres = FromPoints(SendDouble(_printInfo, Selector("rightMargin"))),
                TopMarginMillimetres = FromPoints(SendDouble(_printInfo, Selector("topMargin"))),
                BottomMarginMillimetres = FromPoints(SendDouble(_printInfo, Selector("bottomMargin"))),
                Header = GetControlText(accessory.Header),
                Footer = GetControlText(accessory.Footer),
            };

            settings = ReadAccessoryMargins(accessory, settings);
            ApplySettings(settings);
            Release(accessory.Controller);
            return PlatformPrintResult.Accepted;
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            return PlatformPrintResult.Failed;
        }
    }

    public PlatformPrintResult Print(IntPtr ownerWindow, NotepadPrintDocument document)
    {
        LastError = null;
        try
        {
            EnsurePrintInfo(document.PageSettings);
            ApplySettings(document.PageSettings);
            using var pool = new AutoreleasePool();

            var text = document.Content.Text;
            var textHeight = EstimateTextHeight(text);
            var frame = new NativeRect(0, 0, 612, textHeight);
            var textView = SendIntPtrRect(
                SendIntPtr(GetPrintableTextViewClass(), Selector("alloc")),
                Selector("initWithFrame:"),
                frame);
            PageBorderContexts[textView] = new PageBorderContext(
                document.DisplayName,
                document.PageSettings,
                textHeight,
                DateTime.Now);
            SendVoidBool(textView, Selector("setRichText:"), false);
            SendVoidBool(textView, Selector("setImportsGraphics:"), false);
            SendVoidBool(textView, Selector("setHorizontallyResizable:"), false);
            SendVoidBool(textView, Selector("setVerticallyResizable:"), true);
            SetControlText(textView, text);

            var font = SendIntPtrDoubleDouble(
                GetClass("NSFont"),
                Selector("monospacedSystemFontOfSize:weight:"),
                11,
                0);
            SendVoidIntPtr(textView, Selector("setFont:"), font);

            var operation = SendIntPtrIntPtrIntPtr(
                GetClass("NSPrintOperation"),
                Selector("printOperationWithView:printInfo:"),
                textView,
                _printInfo);
            SendVoidBool(operation, Selector("setShowsPrintPanel:"), true);
            SendVoidBool(operation, Selector("setShowsProgressPanel:"), true);
            var printed = SendBool(operation, Selector("runOperation"));
            PageBorderContexts.TryRemove(textView, out _);
            Release(textView);
            return printed ? PlatformPrintResult.Accepted : PlatformPrintResult.Cancelled;
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            return PlatformPrintResult.Failed;
        }
    }

    private void EnsurePrintInfo(NotepadPageSettings settings)
    {
        if (_printInfo == IntPtr.Zero)
        {
            var shared = SendIntPtr(GetClass("NSPrintInfo"), Selector("sharedPrintInfo"));
            _printInfo = SendIntPtr(shared, Selector("copy"));
        }

        ApplySettings(settings);
    }

    private void ApplySettings(NotepadPageSettings settings)
    {
        SendVoidNInt(_printInfo, Selector("setOrientation:"), settings.Landscape ? 1 : 0);
        SendVoidDouble(_printInfo, Selector("setLeftMargin:"), ToPoints(settings.LeftMarginMillimetres));
        SendVoidDouble(_printInfo, Selector("setRightMargin:"), ToPoints(settings.RightMarginMillimetres));
        SendVoidDouble(_printInfo, Selector("setTopMargin:"), ToPoints(settings.TopMarginMillimetres));
        SendVoidDouble(_printInfo, Selector("setBottomMargin:"), ToPoints(settings.BottomMarginMillimetres));
    }

    private static PageSetupAccessory CreateAccessory(NotepadPageSettings settings)
    {
        const double width = 420;
        const double rowHeight = 25;
        const double labelWidth = 72;
        const double fieldWidth = 310;
        var controller = SendIntPtr(SendIntPtr(GetClass("NSViewController"), Selector("alloc")), Selector("init"));
        var view = SendIntPtrRect(
            SendIntPtr(GetClass("NSView"), Selector("alloc")),
            Selector("initWithFrame:"),
            new NativeRect(0, 0, width, 182));
        SendVoidIntPtr(controller, Selector("setView:"), view);

        AddLabel(view, "Redmond Notepad", 0, 158, width, 20);
        var header = AddField(view, "Header", settings.Header, 126, width, labelWidth, fieldWidth, rowHeight);
        var footer = AddField(view, "Footer", settings.Footer, 96, width, labelWidth, fieldWidth, rowHeight);
        AddLabel(view, "Margins (mm)", 0, 69, width, 20);
        var left = AddCompactField(view, "Left", settings.LeftMarginMillimetres, 39, 0);
        var right = AddCompactField(view, "Right", settings.RightMarginMillimetres, 39, 210);
        var top = AddCompactField(view, "Top", settings.TopMarginMillimetres, 9, 0);
        var bottom = AddCompactField(view, "Bottom", settings.BottomMarginMillimetres, 9, 210);
        Release(view);
        return new PageSetupAccessory(controller, header, footer, left, right, top, bottom);
    }

    private static IntPtr AddField(
        IntPtr view,
        string label,
        string value,
        double y,
        double width,
        double labelWidth,
        double fieldWidth,
        double rowHeight)
    {
        AddLabel(view, label, 0, y + 2, labelWidth, rowHeight);
        var field = CreateTextField(new NativeRect(labelWidth + 8, y, fieldWidth, rowHeight), value);
        SendVoidIntPtr(view, Selector("addSubview:"), field);
        Release(field);
        return field;
    }

    private static IntPtr AddCompactField(IntPtr view, string label, double value, double y, double x)
    {
        AddLabel(view, label, x, y + 2, 54, 25);
        var field = CreateTextField(
            new NativeRect(x + 58, y, 112, 25),
            value.ToString("0.##", CultureInfo.CurrentCulture));
        SendVoidIntPtr(view, Selector("addSubview:"), field);
        Release(field);
        return field;
    }

    private static void AddLabel(IntPtr view, string value, double x, double y, double width, double height)
    {
        var nativeValue = CreateString(value);
        var label = SendIntPtrIntPtr(GetClass("NSTextField"), Selector("labelWithString:"), nativeValue);
        SendVoidRect(label, Selector("setFrame:"), new NativeRect(x, y, width, height));
        SendVoidIntPtr(view, Selector("addSubview:"), label);
        Release(nativeValue);
    }

    private static IntPtr CreateTextField(NativeRect frame, string value)
    {
        var field = SendIntPtrRect(
            SendIntPtr(GetClass("NSTextField"), Selector("alloc")),
            Selector("initWithFrame:"),
            frame);
        SetControlText(field, value);
        return field;
    }

    private static NotepadPageSettings ReadAccessoryMargins(
        PageSetupAccessory accessory,
        NotepadPageSettings settings) =>
        settings with
        {
            LeftMarginMillimetres = ReadPositiveNumber(accessory.LeftMargin, settings.LeftMarginMillimetres),
            RightMarginMillimetres = ReadPositiveNumber(accessory.RightMargin, settings.RightMarginMillimetres),
            TopMarginMillimetres = ReadPositiveNumber(accessory.TopMargin, settings.TopMarginMillimetres),
            BottomMarginMillimetres = ReadPositiveNumber(accessory.BottomMargin, settings.BottomMarginMillimetres),
        };

    private static double ReadPositiveNumber(IntPtr field, double fallback) =>
        double.TryParse(
            GetControlText(field),
            NumberStyles.Float,
            CultureInfo.CurrentCulture,
            out var value)
        && value >= 0
            ? value
            : fallback;

    private static string GetControlText(IntPtr control) =>
        GetString(SendIntPtr(control, Selector("stringValue")));

    private static void SetControlText(IntPtr control, string value)
    {
        var nativeValue = CreateString(value);
        if (control != IntPtr.Zero && IsKindOfClass(control, "NSTextField"))
        {
            SendVoidIntPtr(control, Selector("setStringValue:"), nativeValue);
        }
        else
        {
            SendVoidIntPtr(control, Selector("setString:"), nativeValue);
        }
        Release(nativeValue);
    }

    private static bool IsKindOfClass(IntPtr value, string className) =>
        SendBoolIntPtr(value, Selector("isKindOfClass:"), GetClass(className));

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
        var characters = new char[length];
        if (length > 0)
        {
            CFStringGetCharacters(value, new CFRange(0, length), characters);
        }

        return new string(characters);
    }

    private static double ToPoints(double millimetres) => millimetres * PointsPerMillimetre;

    private static double FromPoints(double points) => Math.Round(points / PointsPerMillimetre, 2);

    private static double EstimateTextHeight(string text)
    {
        const int approximateCharactersPerLine = 86;
        const double lineHeight = 15;
        var visualLines = 0;
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            visualLines += Math.Max(1, (line.Length + approximateCharactersPerLine - 1) / approximateCharactersPerLine);
        }

        return Math.Max(lineHeight, visualLines * lineHeight);
    }

    private static IntPtr GetPrintableTextViewClass()
    {
        if (_printableTextViewClass != IntPtr.Zero)
        {
            return _printableTextViewClass;
        }

        var existing = objc_getClass(PrintableTextViewClassName);
        if (existing != IntPtr.Zero)
        {
            _printableTextViewClass = existing;
            return existing;
        }

        var created = objc_allocateClassPair(GetClass("NSTextView"), PrintableTextViewClassName, 0);
        if (created == IntPtr.Zero)
        {
            throw new InvalidOperationException("Could not create the native printable text view.");
        }

        var implementation = Marshal.GetFunctionPointerForDelegate(DrawPageBorderHandler);
        if (!class_addMethod(
                created,
                Selector("drawPageBorderWithSize:"),
                implementation,
                "v@:{CGSize=dd}"))
        {
            throw new InvalidOperationException("Could not attach the native page-border renderer.");
        }

        objc_registerClassPair(created);
        _printableTextViewClass = created;
        return created;
    }

    private static void DrawPageBorder(IntPtr view, IntPtr selector, NativeSize borderSize)
    {
        try
        {
            if (!PageBorderContexts.TryGetValue(view, out var context))
            {
                return;
            }

            var operation = SendIntPtr(GetClass("NSPrintOperation"), Selector("currentOperation"));
            var pageNumber = Math.Max(1, checked((int)SendNInt(operation, Selector("currentPage"))));
            var printableHeight = Math.Max(
                1,
                borderSize.Height
                    - ToPoints(context.Settings.TopMarginMillimetres)
                    - ToPoints(context.Settings.BottomMarginMillimetres));
            var pageCount = Math.Max(1, (int)Math.Ceiling(context.TextHeight / printableHeight));
            var fieldsContext = new PrintFieldContext(
                context.FileName,
                pageNumber,
                pageCount,
                context.PrintedAt);
            var header = NotepadPrintFieldFormatter.Format(context.Settings.Header, fieldsContext);
            var footer = NotepadPrintFieldFormatter.Format(context.Settings.Footer, fieldsContext);
            var headerBaseline = Math.Max(
                13,
                borderSize.Height - (ToPoints(context.Settings.TopMarginMillimetres) / 2));
            var footerBaseline = Math.Max(
                4,
                (ToPoints(context.Settings.BottomMarginMillimetres) / 2) - 6);
            DrawFieldSegments(header, borderSize.Width, headerBaseline);
            DrawFieldSegments(footer, borderSize.Width, footerBaseline);
        }
        catch
        {
            // Managed exceptions must never cross an Objective-C callback boundary.
        }
    }

    private static void DrawFieldSegments(PrintFieldSegments fields, double width, double y)
    {
        DrawString(fields.Left, 0, y);
        DrawCentredString(fields.Centre, width, y);
        DrawRightAlignedString(fields.Right, width, y);
    }

    private static void DrawCentredString(string value, double width, double y)
    {
        if (value.Length == 0)
        {
            return;
        }

        var nativeValue = CreateString(value);
        var size = SendSizeIntPtr(nativeValue, Selector("sizeWithAttributes:"), IntPtr.Zero);
        SendVoidPointIntPtr(
            nativeValue,
            Selector("drawAtPoint:withAttributes:"),
            new NativePoint(Math.Max(0, (width - size.Width) / 2), y),
            IntPtr.Zero);
        Release(nativeValue);
    }

    private static void DrawRightAlignedString(string value, double width, double y)
    {
        if (value.Length == 0)
        {
            return;
        }

        var nativeValue = CreateString(value);
        var size = SendSizeIntPtr(nativeValue, Selector("sizeWithAttributes:"), IntPtr.Zero);
        SendVoidPointIntPtr(
            nativeValue,
            Selector("drawAtPoint:withAttributes:"),
            new NativePoint(Math.Max(0, width - size.Width), y),
            IntPtr.Zero);
        Release(nativeValue);
    }

    private static void DrawString(string value, double x, double y)
    {
        if (value.Length == 0)
        {
            return;
        }

        var nativeValue = CreateString(value);
        SendVoidPointIntPtr(
            nativeValue,
            Selector("drawAtPoint:withAttributes:"),
            new NativePoint(x, y),
            IntPtr.Zero);
        Release(nativeValue);
    }

    public void Dispose()
    {
        if (_printInfo != IntPtr.Zero)
        {
            Release(_printInfo);
            _printInfo = IntPtr.Zero;
        }
    }

    private static IntPtr GetClass(string name) => objc_getClass(name);
    private static IntPtr Selector(string name) => sel_registerName(name);
    private static void Release(IntPtr value)
    {
        if (value != IntPtr.Zero)
        {
            SendVoid(value, Selector("release"));
        }
    }

    private sealed class AutoreleasePool : IDisposable
    {
        private readonly IntPtr _pool = SendIntPtr(SendIntPtr(GetClass("NSAutoreleasePool"), Selector("alloc")), Selector("init"));
        public void Dispose() => Release(_pool);
    }

    private readonly record struct PageSetupAccessory(
        IntPtr Controller,
        IntPtr Header,
        IntPtr Footer,
        IntPtr LeftMargin,
        IntPtr RightMargin,
        IntPtr TopMargin,
        IntPtr BottomMargin);

    private readonly record struct PageBorderContext(
        string FileName,
        NotepadPageSettings Settings,
        double TextHeight,
        DateTime PrintedAt);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DrawPageBorderCallback(IntPtr view, IntPtr selector, NativeSize borderSize);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(double X, double Y);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeSize(double Width, double Height);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeRect(NativePoint Origin, NativeSize Size)
    {
        public NativeRect(double x, double y, double width, double height)
            : this(new NativePoint(x, y), new NativeSize(width, height))
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct CFRange(nint Location, nint Length);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, nint extraBytes);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool class_addMethod(IntPtr cls, IntPtr selector, IntPtr implementation, string types);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern void objc_registerClassPair(IntPtr cls);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFStringCreateWithCharacters(IntPtr allocator, IntPtr characters, nint length);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern nint CFStringGetLength(IntPtr value);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFStringGetCharacters(IntPtr value, CFRange range, [Out] char[] buffer);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendIntPtr(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendIntPtrIntPtr(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendIntPtrIntPtrIntPtr(IntPtr receiver, IntPtr selector, IntPtr first, IntPtr second);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendIntPtrRect(IntPtr receiver, IntPtr selector, NativeRect frame);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendIntPtrDoubleDouble(IntPtr receiver, IntPtr selector, double first, double second);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern NativeSize SendSizeIntPtr(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendBool(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendBoolIntPtr(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint SendNInt(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint SendNIntIntPtr(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern double SendDouble(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendVoid(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendVoidIntPtr(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendVoidBool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool argument);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendVoidNInt(IntPtr receiver, IntPtr selector, nint argument);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendVoidDouble(IntPtr receiver, IntPtr selector, double argument);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendVoidRect(IntPtr receiver, IntPtr selector, NativeRect argument);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendVoidPointIntPtr(IntPtr receiver, IntPtr selector, NativePoint point, IntPtr argument);
}
