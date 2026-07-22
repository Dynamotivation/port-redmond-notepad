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

    bool IsModified { get; }

    string Text { get; set; }

    string GetText(int offset, int length);

    TextPosition GetPosition(int offset);

    TextReader CreateReader();

    ITextSnapshot CreateSnapshot();

    void WriteTo(TextWriter writer);

    void Replace(int offset, int length, string text);

    void MarkAsOriginal();
}

public readonly record struct TextPosition(int Line, int Column);

public interface ITextSnapshot
{
    int Length { get; }

    string Text { get; }

    TextReader CreateReader();

    void WriteTo(TextWriter writer);
}

public interface ITextBufferFactory
{
    ITextBuffer Create(string initialText = "");
}
