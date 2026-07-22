using System.Text;

namespace Redmond.Notepad.Core;

public static class TextEncoding
{
    public static string GetDisplayName(Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);

        var hasPreamble = encoding.GetPreamble().Length > 0;
        return encoding switch
        {
            UTF8Encoding => hasPreamble ? "UTF-8 BOM" : "UTF-8",
            UnicodeEncoding when encoding.CodePage == 1201 => hasPreamble ? "UTF-16 BE BOM" : "UTF-16 BE",
            UnicodeEncoding => hasPreamble ? "UTF-16 LE BOM" : "UTF-16 LE",
            UTF32Encoding when encoding.CodePage == 12001 => hasPreamble ? "UTF-32 BE BOM" : "UTF-32 BE",
            UTF32Encoding => hasPreamble ? "UTF-32 LE BOM" : "UTF-32 LE",
            _ => encoding.WebName,
        };
    }
}
