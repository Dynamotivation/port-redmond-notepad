using System.Text;

namespace Notepads.Utilities;

public enum LineEnding
{
    Crlf,
    Cr,
    Lf
}

public static class LineEndingUtility
{
    public static LineEnding GetLineEndingTypeFromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n')
            {
                return index > 0 && text[index - 1] == '\r'
                    ? LineEnding.Crlf
                    : LineEnding.Lf;
            }

            if (text[index] == '\r')
            {
                return index + 1 < text.Length && text[index + 1] == '\n'
                    ? LineEnding.Crlf
                    : LineEnding.Cr;
            }
        }

        return LineEnding.Crlf;
    }

    public static string GetLineEndingDisplayText(LineEnding lineEnding) => lineEnding switch
    {
        LineEnding.Cr => "Macintosh (CR)",
        LineEnding.Lf => "Unix (LF)",
        _ => "Windows (CRLF)"
    };

    public static string GetLineEndingName(LineEnding lineEnding) => lineEnding switch
    {
        LineEnding.Cr => "CR",
        LineEnding.Lf => "LF",
        _ => "CRLF"
    };

    public static LineEnding GetLineEndingByName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return name.Trim().ToUpperInvariant() switch
        {
            "CR" => LineEnding.Cr,
            "LF" => LineEnding.Lf,
            _ => LineEnding.Crlf
        };
    }

    public static string ApplyLineEnding(string text, LineEnding lineEnding)
    {
        ArgumentNullException.ThrowIfNull(text);

        var separator = lineEnding switch
        {
            LineEnding.Cr => "\r",
            LineEnding.Lf => "\n",
            _ => "\r\n"
        };

        var converted = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] is '\r' or '\n')
            {
                if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                converted.Append(separator);
            }
            else
            {
                converted.Append(text[index]);
            }
        }

        return converted.ToString();
    }
}
