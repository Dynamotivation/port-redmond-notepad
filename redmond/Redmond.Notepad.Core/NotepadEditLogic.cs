using System.Globalization;

namespace Redmond.Notepad.Core;

/// <summary>
/// Portable adaptations of the selection/search and Time/Date behavior from
/// Notepads' UWP TextEditorCore.
/// </summary>
public static class NotepadEditLogic
{
    public static string GetSearchText(ITextBuffer buffer, int selectionStart, int selectionLength)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        selectionStart = Math.Clamp(selectionStart, 0, buffer.Length);
        selectionLength = Math.Clamp(selectionLength, 0, buffer.Length - selectionStart);

        var selectedText = buffer.GetText(selectionStart, selectionLength).Trim();
        if (selectedText.IndexOfAny(['\r', '\n']) >= 0)
        {
            return string.Empty;
        }

        if (selectedText.Length > 0 || selectionStart >= buffer.Length)
        {
            return selectedText;
        }

        var wordPosition = selectionStart;
        if (!IsLetterOrDigitAt(buffer, wordPosition))
        {
            return string.Empty;
        }

        var start = wordPosition;
        while (start > 0 && IsLetterOrDigitAt(buffer, start - 1))
        {
            start--;
        }

        var end = wordPosition + 1;
        while (end < buffer.Length && IsLetterOrDigitAt(buffer, end))
        {
            end++;
        }

        return buffer.GetText(start, end - start);
    }

    public static string GetDateTimeText(DateTime? value = null, CultureInfo? culture = null) =>
        (value ?? DateTime.Now).ToString(culture ?? CultureInfo.CurrentCulture);

    private static bool IsLetterOrDigitAt(ITextBuffer buffer, int offset) =>
        offset >= 0
        && offset < buffer.Length
        && char.IsLetterOrDigit(buffer.GetText(offset, 1)[0]);
}
