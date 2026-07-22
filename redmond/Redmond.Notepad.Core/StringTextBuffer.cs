namespace Redmond.Notepad.Core;

/// <summary>
/// Dependency-free fallback buffer for core tests and non-UI consumers.
/// Desktop frontends should provide an incremental buffer implementation.
/// </summary>
public sealed class StringTextBuffer(string initialText = "") : ITextBuffer
{
    private string _text = initialText ?? string.Empty;
    private bool _isModified;

    public event EventHandler? Changed;

    public int Length => _text.Length;

    public int LineCount
    {
        get
        {
            var count = 1;
            for (var index = 0; index < _text.Length; index++)
            {
                if (_text[index] == '\r')
                {
                    count++;
                    if (index + 1 < _text.Length && _text[index + 1] == '\n')
                    {
                        index++;
                    }
                }
                else if (_text[index] == '\n')
                {
                    count++;
                }
            }

            return count;
        }
    }

    public bool IsModified => _isModified;

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
            _isModified = true;
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
            if (_text[index] == '\r')
            {
                line++;
                column = 1;
                if (index + 1 < offset && _text[index + 1] == '\n')
                {
                    index++;
                }
            }
            else if (_text[index] == '\n')
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

    public string GetText(int offset, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (offset > _text.Length || length > _text.Length - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        return _text.Substring(offset, length);
    }

    public TextReader CreateReader() => new StringReader(_text);

    public ITextSnapshot CreateSnapshot() => new StringTextSnapshot(_text);

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

    public void MarkAsOriginal() => _isModified = false;

    private sealed class StringTextSnapshot(string text) : ITextSnapshot
    {
        public int Length => text.Length;

        public string Text => text;

        public TextReader CreateReader() => new StringReader(text);

        public void WriteTo(TextWriter writer) => writer.Write(text);
    }
}

public sealed class StringTextBufferFactory : ITextBufferFactory
{
    public ITextBuffer Create(string initialText = "") => new StringTextBuffer(initialText);
}
