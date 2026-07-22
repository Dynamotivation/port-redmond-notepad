namespace Redmond.Notepad.Core;

/// <summary>
/// Dependency-free fallback buffer for core tests and non-UI consumers.
/// Desktop frontends should provide an incremental buffer implementation.
/// </summary>
public sealed class StringTextBuffer(string initialText = "") : ITextBuffer
{
    private string _text = initialText ?? string.Empty;

    public event EventHandler? Changed;

    public int Length => _text.Length;

    public int LineCount => _text.Length == 0
        ? 1
        : _text.Count(character => character == '\n') + 1;

    public string Text
    {
        get => _text;
        set
        {
            value ??= string.Empty;
            if (_text == value)
            {
                return;
            }

            _text = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public TextPosition GetPosition(int offset)
    {
        offset = Math.Clamp(offset, 0, _text.Length);
        var line = 1;
        var column = 1;
        for (var index = 0; index < offset; index++)
        {
            if (_text[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return new TextPosition(line, column);
    }

    public TextReader CreateReader() => new StringReader(_text);

    public void WriteTo(TextWriter writer) => writer.Write(_text);

    public void Replace(int offset, int length, string text)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (offset > _text.Length || length > _text.Length - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        Text = string.Concat(_text.AsSpan(0, offset), text ?? string.Empty, _text.AsSpan(offset + length));
    }
}

public sealed class StringTextBufferFactory : ITextBufferFactory
{
    public ITextBuffer Create(string initialText = "") => new StringTextBuffer(initialText);
}
