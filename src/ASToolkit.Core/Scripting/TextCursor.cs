namespace ASToolkit.Core.Scripting;

internal ref struct TextCursor
{
    private readonly ReadOnlySpan<char> _text;

    public TextCursor(ReadOnlySpan<char> text, int position)
    {
        _text = text;
        Position = position;
    }

    public int Position { get; private set; }

    public readonly ReadOnlySpan<char> Text => _text;

    public readonly bool IsAtEnd => Position >= _text.Length;

    public static bool IsWord(char character) => char.IsLetterOrDigit(character) || character == '_';

    public readonly bool IsWordStart => Position == 0 || !IsWord(_text[Position - 1]);

    public void SkipSpace()
    {
        while (Position < _text.Length && char.IsWhiteSpace(_text[Position]))
        {
            Position++;
        }
    }

    public bool Read(ReadOnlySpan<char> literal)
    {
        if (!_text[Position..].StartsWith(literal, StringComparison.Ordinal))
            return false;

        Position += literal.Length;
        return true;
    }

    public bool ReadAfterSpace(ReadOnlySpan<char> literal)
    {
        SkipSpace();
        return Read(literal);
    }

    public bool ReadInteger(bool signed, out int value)
    {
        int start = Position;
        if (signed && Position < _text.Length && _text[Position] == '-')
        {
            Position++;
        }

        int digits = Position;
        while (Position < _text.Length && char.IsDigit(_text[Position]))
        {
            Position++;
        }

        if (Position == digits)
        {
            Position = start;
            value = 0;
            return false;
        }

        value = int.Parse(_text[start..Position]);
        return true;
    }

    public bool ReadIntegerAfterSpace(bool signed, out int value)
    {
        SkipSpace();
        return ReadInteger(signed, out value);
    }

    public bool ReadName(out ReadOnlySpan<char> name)
    {
        int start = Position;
        if (Position < _text.Length && (char.IsAsciiLetter(_text[Position]) || _text[Position] == '_'))
        {
            Position++;
            while (Position < _text.Length && IsWord(_text[Position]))
            {
                Position++;
            }
        }

        name = _text[start..Position];
        return Position > start;
    }

    public readonly char Peek() => Position < _text.Length ? _text[Position] : '\0';
}
