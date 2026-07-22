using AvaloniaEdit.Document;
using Redmond.Notepad.Core;

namespace Redmond.Notepad.Editor.AvaloniaEdit;

public sealed class AvaloniaEditTextBuffer : ITextBuffer
{
    public AvaloniaEditTextBuffer(string initialText = "")
    {
        Document = new TextDocument(initialText ?? string.Empty);
        Document.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Changed;

    public TextDocument Document { get; }

    public int Length => Document.TextLength;

    public int LineCount => Document.LineCount;

    public bool IsModified => !Document.UndoStack.IsOriginalFile;

    public string Text
    {
        get => Document.Text;
        set => Document.Text = value ?? string.Empty;
    }

    public TextPosition GetPosition(int offset)
    {
        var location = Document.GetLocation(Math.Clamp(offset, 0, Document.TextLength));
        return new TextPosition(location.Line, location.Column);
    }

    public string GetText(int offset, int length) => Document.GetText(offset, length);

    public TextReader CreateReader() => Document.CreateReader();

    public ITextSnapshot CreateSnapshot() => new AvaloniaEditTextSnapshot(Document.CreateSnapshot());

    public void WriteTo(TextWriter writer) => Document.WriteTextTo(writer);

    public void Replace(int offset, int length, string text) =>
        Document.Replace(offset, length, text ?? string.Empty);

    public void MarkAsOriginal()
    {
        Document.UndoStack.MarkAsOriginalFile();
    }

    private sealed class AvaloniaEditTextSnapshot(ITextSource source) : ITextSnapshot
    {
        public int Length => source.TextLength;

        public string Text => source.Text;

        public TextReader CreateReader() => source.CreateReader();

        public void WriteTo(TextWriter writer) => source.WriteTextTo(writer);
    }
}

public sealed class AvaloniaEditTextBufferFactory : ITextBufferFactory
{
    public ITextBuffer Create(string initialText = "") => new AvaloniaEditTextBuffer(initialText);
}
