using System.Globalization;

namespace Redmond.Notepad.Core;

public sealed record NotepadPageSettings
{
    public const double DefaultHorizontalMarginMillimetres = 20;
    public const double DefaultVerticalMarginMillimetres = 25;

    public string? PaperName { get; init; }

    public bool Landscape { get; init; }

    public double LeftMarginMillimetres { get; init; } = DefaultHorizontalMarginMillimetres;

    public double RightMarginMillimetres { get; init; } = DefaultHorizontalMarginMillimetres;

    public double TopMarginMillimetres { get; init; } = DefaultVerticalMarginMillimetres;

    public double BottomMarginMillimetres { get; init; } = DefaultVerticalMarginMillimetres;

    public string Header { get; init; } = "&f";

    public string Footer { get; init; } = "Page &p";
}

public sealed record NotepadPrintDocument(
    string DisplayName,
    ITextSnapshot Content,
    NotepadPageSettings PageSettings);

public readonly record struct PrintFieldContext(
    string FileName,
    int PageNumber,
    int PageCount,
    DateTime PrintedAt);

public readonly record struct PrintFieldSegments(string Left, string Centre, string Right);

public static class NotepadPrintFieldFormatter
{
    public static PrintFieldSegments Format(
        string? template,
        PrintFieldContext context,
        CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var segments = new[] { new System.Text.StringBuilder(), new System.Text.StringBuilder(), new System.Text.StringBuilder() };
        var alignment = 1;
        var value = template ?? string.Empty;

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character != '&' || index + 1 >= value.Length)
            {
                segments[alignment].Append(character);
                continue;
            }

            var command = value[++index];
            switch (char.ToLowerInvariant(command))
            {
                case 'l':
                    alignment = 0;
                    break;
                case 'c':
                    alignment = 1;
                    break;
                case 'r':
                    alignment = 2;
                    break;
                case 'f':
                    segments[alignment].Append(context.FileName);
                    break;
                case 'p':
                    segments[alignment].Append(
                        command == 'P'
                            ? context.PageCount.ToString(culture)
                            : context.PageNumber.ToString(culture));
                    break;
                case 'd':
                    segments[alignment].Append(context.PrintedAt.ToString("d", culture));
                    break;
                case 't':
                    segments[alignment].Append(context.PrintedAt.ToString("t", culture));
                    break;
                case '&':
                    segments[alignment].Append('&');
                    break;
                default:
                    segments[alignment].Append('&').Append(command);
                    break;
            }
        }

        return new PrintFieldSegments(
            segments[0].ToString(),
            segments[1].ToString(),
            segments[2].ToString());
    }
}
