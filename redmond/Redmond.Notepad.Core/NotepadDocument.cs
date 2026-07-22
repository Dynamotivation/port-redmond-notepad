using Notepads.Utilities;

namespace Redmond.Notepad.Core;

public sealed class NotepadDocument
{
    public const string UntitledName = "Untitled";

    public NotepadDocument(ITextBuffer buffer)
    {
        Buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
    }

    public ITextBuffer Buffer { get; }

    public string Text => Buffer.Text;

    public string DisplayName { get; private set; } = UntitledName;

    public LineEnding LineEnding { get; private set; } = LineEnding.Crlf;

    public string LineEndingDisplayText => LineEndingUtility.GetLineEndingDisplayText(LineEnding);

    public int CharacterCount => Buffer.Length;

    public int LineCount => Buffer.LineCount;

    public void LoadText(string? text)
    {
        var loadedText = text ?? string.Empty;
        LineEnding = LineEndingUtility.GetLineEndingTypeFromText(loadedText);
        Buffer.Text = loadedText;
    }
}
