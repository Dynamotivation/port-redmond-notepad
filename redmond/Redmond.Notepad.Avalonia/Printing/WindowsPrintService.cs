using System.ComponentModel;
using System.Runtime.InteropServices;
using Redmond.Notepad.Core;

namespace Redmond.Notepad.Avalonia.Printing;

/// <summary>
/// Uses the Windows common page-setup and print property-sheet APIs. Windows is
/// responsible for the UI; this class supplies Notepad's document and layout.
/// </summary>
internal sealed class WindowsPrintService : IPlatformPrintService
{
    private const uint PsdMargins = 0x00000002;
    private const uint PsdInHundredthsOfMillimetres = 0x00000008;
    private const uint PsdEnablePageSetupHook = 0x00002000;
    private const uint PdNoSelection = 0x00000004;
    private const uint PdNoPageNumbers = 0x00000008;
    private const uint PdReturnDc = 0x00000100;
    private const uint PdUseDevModeCopiesAndCollate = 0x00040000;
    private const uint PdResultPrint = 1;
    private const int LogPixelsX = 88;
    private const int LogPixelsY = 90;
    private const int HorzRes = 8;
    private const int VertRes = 10;
    private const int PhysicalOffsetX = 112;
    private const int PhysicalOffsetY = 113;
    private const uint Transparent = 1;
    private const uint TaLeft = 0;
    private const uint TaRight = 2;
    private const uint TaCenter = 6;
    private const uint WmInitDialog = 0x0110;
    private const uint WmCommand = 0x0111;
    private const uint WmGetFont = 0x0031;
    private const uint WmSetFont = 0x0030;
    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsTabStop = 0x00010000;
    private const uint WsBorder = 0x00800000;
    private const uint EsAutoHScroll = 0x0080;
    private const uint SwpNoZOrder = 0x0004;
    private const int IdOk = 1;
    private const int IdCancel = 2;
    private const int AddedPageSetupHeight = 118;
    private static readonly PageSetupHookCallback PageSetupHookHandler = PageSetupHook;

    private IntPtr _deviceMode;
    private IntPtr _deviceNames;

    public string? LastError { get; private set; }

    public PlatformPrintResult ShowPageSetup(IntPtr ownerWindow, ref NotepadPageSettings settings)
    {
        LastError = null;
        var accessory = new WindowsPageSetupAccessory(settings.Header, settings.Footer);
        var accessoryHandle = GCHandle.Alloc(accessory);
        var dialog = new PageSetupDialog
        {
            Size = (uint)Marshal.SizeOf<PageSetupDialog>(),
            Owner = ownerWindow,
            DeviceMode = _deviceMode,
            DeviceNames = _deviceNames,
            Flags = PsdMargins | PsdInHundredthsOfMillimetres | PsdEnablePageSetupHook,
            Margin = new NativeRect(
                ToHundredthsOfMillimetres(settings.LeftMarginMillimetres),
                ToHundredthsOfMillimetres(settings.TopMarginMillimetres),
                ToHundredthsOfMillimetres(settings.RightMarginMillimetres),
                ToHundredthsOfMillimetres(settings.BottomMarginMillimetres)),
            CustomData = GCHandle.ToIntPtr(accessoryHandle),
            PageSetupHook = Marshal.GetFunctionPointerForDelegate(PageSetupHookHandler),
        };

        bool accepted;
        try
        {
            accepted = PageSetupDlgW(ref dialog);
        }
        finally
        {
            accessoryHandle.Free();
        }

        if (!accepted)
        {
            var error = CommDlgExtendedError();
            if (error != 0)
            {
                LastError = $"Windows Page Setup failed with common-dialog error 0x{error:X}.";
                return PlatformPrintResult.Failed;
            }

            return PlatformPrintResult.Cancelled;
        }

        _deviceMode = dialog.DeviceMode;
        _deviceNames = dialog.DeviceNames;
        settings = settings with
        {
            LeftMarginMillimetres = FromHundredthsOfMillimetres(dialog.Margin.Left),
            TopMarginMillimetres = FromHundredthsOfMillimetres(dialog.Margin.Top),
            RightMarginMillimetres = FromHundredthsOfMillimetres(dialog.Margin.Right),
            BottomMarginMillimetres = FromHundredthsOfMillimetres(dialog.Margin.Bottom),
            Landscape = ReadLandscape(dialog.DeviceMode, settings.Landscape),
            Header = accessory.Header,
            Footer = accessory.Footer,
        };
        return PlatformPrintResult.Accepted;
    }

    private static UIntPtr PageSetupHook(IntPtr dialogWindow, uint message, UIntPtr wordParameter, IntPtr longParameter)
    {
        try
        {
            if (message == WmInitDialog)
            {
                var setup = Marshal.PtrToStructure<PageSetupDialog>(longParameter);
                var handle = GCHandle.FromIntPtr(setup.CustomData);
                if (handle.Target is WindowsPageSetupAccessory accessory)
                {
                    CreatePageSetupAccessory(dialogWindow, accessory);
                }
            }
            else if (message == WmCommand && LowWord(wordParameter) == IdOk)
            {
                if (PageSetupAccessories.TryGetValue(dialogWindow, out var accessory))
                {
                    accessory.Header = GetWindowText(accessory.HeaderField);
                    accessory.Footer = GetWindowText(accessory.FooterField);
                    PageSetupAccessories.Remove(dialogWindow);
                }
            }
            else if (message == WmCommand && LowWord(wordParameter) == IdCancel)
            {
                PageSetupAccessories.Remove(dialogWindow);
            }
        }
        catch
        {
            // Native dialog callbacks must never let managed exceptions escape.
        }

        return UIntPtr.Zero;
    }

    private static readonly Dictionary<IntPtr, WindowsPageSetupAccessory> PageSetupAccessories = [];

    private static void CreatePageSetupAccessory(IntPtr dialogWindow, WindowsPageSetupAccessory accessory)
    {
        GetWindowRect(dialogWindow, out var windowRect);
        GetClientRect(dialogWindow, out var clientRect);
        var originalClientBottom = clientRect.Bottom;
        SetWindowPos(
            dialogWindow,
            IntPtr.Zero,
            0,
            0,
            windowRect.Right - windowRect.Left,
            windowRect.Bottom - windowRect.Top + AddedPageSetupHeight,
            SwpNoZOrder);

        MoveControlDown(dialogWindow, IdOk, AddedPageSetupHeight);
        MoveControlDown(dialogWindow, IdCancel, AddedPageSetupHeight);

        var font = SendMessageW(dialogWindow, WmGetFont, UIntPtr.Zero, IntPtr.Zero);
        AddStatic(dialogWindow, "Header:", 13, originalClientBottom + 9, 58, 23, font);
        accessory.HeaderField = AddEdit(
            dialogWindow,
            accessory.Header,
            74,
            originalClientBottom + 7,
            Math.Max(120, clientRect.Right - 87),
            23,
            font);
        AddStatic(dialogWindow, "Footer:", 13, originalClientBottom + 39, 58, 23, font);
        accessory.FooterField = AddEdit(
            dialogWindow,
            accessory.Footer,
            74,
            originalClientBottom + 37,
            Math.Max(120, clientRect.Right - 87),
            23,
            font);
        AddStatic(
            dialogWindow,
            "Input values: &f file, &p page, &P page count, &d date, &t time; &l/&c/&r align",
            74,
            originalClientBottom + 68,
            Math.Max(180, clientRect.Right - 87),
            20,
            font);
        PageSetupAccessories[dialogWindow] = accessory;
    }

    private static IntPtr AddEdit(
        IntPtr parent,
        string value,
        int x,
        int y,
        int width,
        int height,
        IntPtr font)
    {
        var control = CreateWindowExW(
            0,
            "EDIT",
            value,
            WsChild | WsVisible | WsTabStop | WsBorder | EsAutoHScroll,
            x,
            y,
            width,
            height,
            parent,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
        SendMessageW(control, WmSetFont, (UIntPtr)(nuint)font, (IntPtr)1);
        return control;
    }

    private static void AddStatic(
        IntPtr parent,
        string value,
        int x,
        int y,
        int width,
        int height,
        IntPtr font)
    {
        var control = CreateWindowExW(
            0,
            "STATIC",
            value,
            WsChild | WsVisible,
            x,
            y,
            width,
            height,
            parent,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
        SendMessageW(control, WmSetFont, (UIntPtr)(nuint)font, (IntPtr)1);
    }

    private static void MoveControlDown(IntPtr parent, int identifier, int offset)
    {
        var control = GetDlgItem(parent, identifier);
        if (control == IntPtr.Zero || !GetWindowRect(control, out var rect))
        {
            return;
        }

        var points = new[]
        {
            new NativePoint { X = rect.Left, Y = rect.Top },
            new NativePoint { X = rect.Right, Y = rect.Bottom },
        };
        MapWindowPoints(IntPtr.Zero, parent, points, 2);
        SetWindowPos(
            control,
            IntPtr.Zero,
            points[0].X,
            points[0].Y + offset,
            points[1].X - points[0].X,
            points[1].Y - points[0].Y,
            SwpNoZOrder);
    }

    private static int LowWord(UIntPtr value) => unchecked((ushort)value.ToUInt64());

    private static string GetWindowText(IntPtr window)
    {
        var length = GetWindowTextLengthW(window);
        var buffer = new char[length + 1];
        GetWindowTextW(window, buffer, buffer.Length);
        return new string(buffer, 0, length);
    }

    public PlatformPrintResult Print(IntPtr ownerWindow, NotepadPrintDocument document)
    {
        LastError = null;
        var dialog = new PrintDialogEx
        {
            Size = (uint)Marshal.SizeOf<PrintDialogEx>(),
            Owner = ownerWindow,
            DeviceMode = _deviceMode,
            DeviceNames = _deviceNames,
            Flags = PdNoSelection | PdNoPageNumbers | PdReturnDc | PdUseDevModeCopiesAndCollate,
            MinimumPage = 1,
            MaximumPage = uint.MaxValue,
            Copies = 1,
            StartPage = uint.MaxValue,
        };

        var result = PrintDlgExW(ref dialog);
        if (result != 0)
        {
            LastError = $"Windows Print failed with HRESULT 0x{result:X8}.";
            return PlatformPrintResult.Failed;
        }

        _deviceMode = dialog.DeviceMode;
        _deviceNames = dialog.DeviceNames;
        if (dialog.ResultAction != PdResultPrint)
        {
            if (dialog.DeviceContext != IntPtr.Zero)
            {
                DeleteDC(dialog.DeviceContext);
            }

            return PlatformPrintResult.Cancelled;
        }

        try
        {
            PrintToDeviceContext(dialog.DeviceContext, document);
            return PlatformPrintResult.Accepted;
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            return PlatformPrintResult.Failed;
        }
        finally
        {
            if (dialog.DeviceContext != IntPtr.Zero)
            {
                DeleteDC(dialog.DeviceContext);
            }
        }
    }

    private static void PrintToDeviceContext(IntPtr deviceContext, NotepadPrintDocument document)
    {
        var dpiX = GetDeviceCaps(deviceContext, LogPixelsX);
        var dpiY = GetDeviceCaps(deviceContext, LogPixelsY);
        var pageWidth = GetDeviceCaps(deviceContext, HorzRes);
        var pageHeight = GetDeviceCaps(deviceContext, VertRes);
        var physicalOffsetX = GetDeviceCaps(deviceContext, PhysicalOffsetX);
        var physicalOffsetY = GetDeviceCaps(deviceContext, PhysicalOffsetY);
        var settings = document.PageSettings;

        var left = Math.Max(0, MillimetresToPixels(settings.LeftMarginMillimetres, dpiX) - physicalOffsetX);
        var right = Math.Max(0, MillimetresToPixels(settings.RightMarginMillimetres, dpiX) - physicalOffsetX);
        var top = Math.Max(0, MillimetresToPixels(settings.TopMarginMillimetres, dpiY) - physicalOffsetY);
        var bottom = Math.Max(0, MillimetresToPixels(settings.BottomMarginMillimetres, dpiY) - physicalOffsetY);
        var contentWidth = Math.Max(1, pageWidth - left - right);
        var contentHeight = Math.Max(1, pageHeight - top - bottom);

        var font = CreateFontW(
            -MulDiv(10, dpiY, 72),
            0,
            0,
            0,
            400,
            0,
            0,
            0,
            1,
            0,
            0,
            0,
            0x31,
            "Consolas");
        if (font == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the print font.");
        }

        var previousFont = SelectObject(deviceContext, font);
        try
        {
            if (!GetTextMetricsW(deviceContext, out var metrics))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not measure the print font.");
            }

            var lineHeight = Math.Max(1, metrics.Height + metrics.ExternalLeading);
            var charactersPerLine = Math.Max(1, contentWidth / Math.Max(1, metrics.AverageCharacterWidth));
            var headerPresent = !string.IsNullOrEmpty(settings.Header);
            var footerPresent = !string.IsNullOrEmpty(settings.Footer);
            var bodyTop = top + (headerPresent ? lineHeight * 2 : 0);
            var bodyBottom = pageHeight - bottom - (footerPresent ? lineHeight * 2 : 0);
            var linesPerPage = Math.Max(1, (bodyBottom - bodyTop) / lineHeight);
            var visualLineCount = CountVisualLines(document.Content, charactersPerLine);
            var pageCount = Math.Max(1, (visualLineCount + linesPerPage - 1) / linesPerPage);

            var info = new DocumentInfo
            {
                Size = Marshal.SizeOf<DocumentInfo>(),
                DocumentName = document.DisplayName,
            };
            if (StartDocW(deviceContext, ref info) <= 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not start the print job.");
            }

            try
            {
                SetBkMode(deviceContext, Transparent);
                using var reader = document.Content.CreateReader();
                using var lines = EnumerateVisualLines(reader, charactersPerLine).GetEnumerator();
                var hasLine = lines.MoveNext();
                for (var page = 1; page <= pageCount; page++)
                {
                    if (StartPage(deviceContext) <= 0)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not start a printed page.");
                    }

                    DrawFields(deviceContext, settings.Header, document.DisplayName, page, pageCount, left, pageWidth - right, top, lineHeight);
                    var y = bodyTop;
                    for (var line = 0; line < linesPerPage && hasLine; line++)
                    {
                        DrawTextLine(deviceContext, left, y, lines.Current);
                        y += lineHeight;
                        hasLine = lines.MoveNext();
                    }

                    DrawFields(deviceContext, settings.Footer, document.DisplayName, page, pageCount, left, pageWidth - right, pageHeight - bottom - lineHeight, lineHeight);
                    if (EndPage(deviceContext) <= 0)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not finish a printed page.");
                    }
                }
            }
            catch
            {
                AbortDoc(deviceContext);
                throw;
            }

            if (EndDoc(deviceContext) <= 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not finish the print job.");
            }
        }
        finally
        {
            SelectObject(deviceContext, previousFont);
            DeleteObject(font);
        }
    }

    private static int CountVisualLines(ITextSnapshot snapshot, int charactersPerLine)
    {
        using var reader = snapshot.CreateReader();
        var count = 0;
        foreach (var _ in EnumerateVisualLines(reader, charactersPerLine))
        {
            count++;
        }

        return Math.Max(1, count);
    }

    private static IEnumerable<string> EnumerateVisualLines(TextReader reader, int charactersPerLine)
    {
        while (reader.ReadLine() is { } line)
        {
            var expanded = line.Replace("\t", "    ", StringComparison.Ordinal);
            if (expanded.Length == 0)
            {
                yield return string.Empty;
                continue;
            }

            for (var offset = 0; offset < expanded.Length; offset += charactersPerLine)
            {
                yield return expanded.Substring(offset, Math.Min(charactersPerLine, expanded.Length - offset));
            }
        }
    }

    private static void DrawFields(
        IntPtr deviceContext,
        string template,
        string fileName,
        int page,
        int pageCount,
        int left,
        int right,
        int y,
        int lineHeight)
    {
        var fields = NotepadPrintFieldFormatter.Format(
            template,
            new PrintFieldContext(fileName, page, pageCount, DateTime.Now));
        DrawAligned(deviceContext, fields.Left, left, y, TaLeft);
        DrawAligned(deviceContext, fields.Centre, left + ((right - left) / 2), y, TaCenter);
        DrawAligned(deviceContext, fields.Right, right, y, TaRight);
    }

    private static void DrawAligned(IntPtr deviceContext, string value, int x, int y, uint alignment)
    {
        if (value.Length == 0)
        {
            return;
        }

        var previous = SetTextAlign(deviceContext, alignment);
        TextOutW(deviceContext, x, y, value, value.Length);
        SetTextAlign(deviceContext, previous);
    }

    private static void DrawTextLine(IntPtr deviceContext, int x, int y, string value)
    {
        SetTextAlign(deviceContext, TaLeft);
        TextOutW(deviceContext, x, y, value, value.Length);
    }

    private static bool ReadLandscape(IntPtr deviceMode, bool fallback)
    {
        if (deviceMode == IntPtr.Zero)
        {
            return fallback;
        }

        var pointer = GlobalLock(deviceMode);
        if (pointer == IntPtr.Zero)
        {
            return fallback;
        }

        try
        {
            return Marshal.ReadInt16(pointer, 76) == 2;
        }
        finally
        {
            GlobalUnlock(deviceMode);
        }
    }

    private static int MillimetresToPixels(double value, int dpi) =>
        (int)Math.Round(value * dpi / 25.4);

    private static int ToHundredthsOfMillimetres(double value) => (int)Math.Round(value * 100);

    private static double FromHundredthsOfMillimetres(int value) => value / 100.0;

    public void Dispose()
    {
        if (_deviceMode != IntPtr.Zero)
        {
            GlobalFree(_deviceMode);
            _deviceMode = IntPtr.Zero;
        }

        if (_deviceNames != IntPtr.Zero)
        {
            GlobalFree(_deviceNames);
            _deviceNames = IntPtr.Zero;
        }
    }

    private sealed class WindowsPageSetupAccessory(string header, string footer)
    {
        public string Header { get; set; } = header;
        public string Footer { get; set; } = footer;
        public IntPtr HeaderField { get; set; }
        public IntPtr FooterField { get; set; }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate UIntPtr PageSetupHookCallback(
        IntPtr dialogWindow,
        uint message,
        UIntPtr wordParameter,
        IntPtr longParameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect(int left, int top, int right, int bottom)
    {
        public int Left = left;
        public int Top = top;
        public int Right = right;
        public int Bottom = bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PageSetupDialog
    {
        public uint Size;
        public IntPtr Owner;
        public IntPtr DeviceMode;
        public IntPtr DeviceNames;
        public uint Flags;
        public NativePoint PaperSize;
        public NativeRect MinimumMargin;
        public NativeRect Margin;
        public IntPtr Instance;
        public IntPtr CustomData;
        public IntPtr PageSetupHook;
        public IntPtr PagePaintHook;
        public IntPtr PageSetupTemplateName;
        public IntPtr PageSetupTemplate;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PrintDialogEx
    {
        public uint Size;
        public IntPtr Owner;
        public IntPtr DeviceMode;
        public IntPtr DeviceNames;
        public IntPtr DeviceContext;
        public uint Flags;
        public uint Flags2;
        public uint ExclusionFlags;
        public uint PageRangeCount;
        public uint MaximumPageRangeCount;
        public IntPtr PageRanges;
        public uint MinimumPage;
        public uint MaximumPage;
        public uint Copies;
        public IntPtr Instance;
        public IntPtr PrintTemplateName;
        public IntPtr Callback;
        public uint PropertyPageCount;
        public IntPtr PropertyPages;
        public uint StartPage;
        public uint ResultAction;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DocumentInfo
    {
        public int Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string DocumentName;
        public IntPtr Output;
        public IntPtr DataType;
        public uint Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TextMetrics
    {
        public int Height;
        public int Ascent;
        public int Descent;
        public int InternalLeading;
        public int ExternalLeading;
        public int AverageCharacterWidth;
        public int MaximumCharacterWidth;
        public int Weight;
        public int Overhang;
        public int DigitizedAspectX;
        public int DigitizedAspectY;
        public char FirstCharacter;
        public char LastCharacter;
        public char DefaultCharacter;
        public char BreakCharacter;
        public byte Italic;
        public byte Underlined;
        public byte StruckOut;
        public byte PitchAndFamily;
        public byte CharacterSet;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PageSetupDlgW(ref PageSetupDialog dialog);

    [DllImport("comdlg32.dll")]
    private static extern uint CommDlgExtendedError();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDlgItem(IntPtr dialog, int identifier);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern int MapWindowPoints(IntPtr from, IntPtr to, [In, Out] NativePoint[] points, uint pointCount);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageW(IntPtr window, uint message, UIntPtr wordParameter, IntPtr longParameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr window, [Out] char[] value, int maximumCount);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)]
    private static extern int PrintDlgExW(ref PrintDialogEx dialog);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr memory);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr deviceContext, int index);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFontW(
        int height,
        int width,
        int escapement,
        int orientation,
        int weight,
        uint italic,
        uint underline,
        uint strikeOut,
        uint characterSet,
        uint outputPrecision,
        uint clipPrecision,
        uint quality,
        uint pitchAndFamily,
        string faceName);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr value);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr value);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTextMetricsW(IntPtr deviceContext, out TextMetrics metrics);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(IntPtr deviceContext, uint mode);

    [DllImport("gdi32.dll")]
    private static extern uint SetTextAlign(IntPtr deviceContext, uint alignment);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TextOutW(IntPtr deviceContext, int x, int y, string value, int length);

    [DllImport("gdi32.dll")]
    private static extern int MulDiv(int number, int numerator, int denominator);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int StartDocW(IntPtr deviceContext, ref DocumentInfo info);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int StartPage(IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int EndPage(IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int EndDoc(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern int AbortDoc(IntPtr deviceContext);
}
