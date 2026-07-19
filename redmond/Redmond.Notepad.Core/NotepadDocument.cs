using Notepads.Utilities;

namespace Redmond.Notepad.Core;

public sealed class NotepadDocument
{
    public const string UntitledName = "Untitled";

    public string Text { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = UntitledName;

    public LineEnding LineEnding { get; private set; } = LineEnding.Crlf;

    public string LineEndingDisplayText => LineEndingUtility.GetLineEndingDisplayText(LineEnding);

    public int CharacterCount => Text.Length;

    public int LineCount => Text.Length == 0
        ? 1
        : Text.Count(character => character == '\n') + 1;

    public void ReplaceText(string? text)
    {
        Text = text ?? string.Empty;
        LineEnding = LineEndingUtility.GetLineEndingTypeFromText(Text);
    }
}
