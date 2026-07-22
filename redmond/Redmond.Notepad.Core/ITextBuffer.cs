namespace Redmond.Notepad.Core;

/// <summary>
/// Platform-neutral text storage used by a Notepad document.
/// Implementations may use a rope, piece table, or another incremental structure.
/// </summary>
public interface ITextBuffer
{
    event EventHandler? Changed;

    int Length { get; }

    int LineCount { get; }

    string Text { get; set; }

    TextPosition GetPosition(int offset);

    TextReader CreateReader();

    void WriteTo(TextWriter writer);

    void Replace(int offset, int length, string text);
}

public readonly record struct TextPosition(int Line, int Column);

public interface ITextBufferFactory
{
    ITextBuffer Create(string initialText = "");
}
