namespace ASToolkit.Engine.IO;

internal sealed class ScriptFile : IDisposable
{
    private const int LineChunkSize = 255;
    private const byte CarriageReturn = 13;
    private const byte LineFeed = 10;
    private const byte EndOfText = 0x1a;

    private readonly byte[] _data;
    private readonly Stream? _writer;
    private int _position;

    public ScriptFile(ReadOnlySpan<byte> data, Stream? writer)
    {
        _data = TranslateText(data);
        _writer = writer;
    }

    public bool IsEnd { get; private set; }

    public int ReadChar()
    {
        if (_position >= _data.Length)
        {
            IsEnd = true;

            return -1;
        }

        return _data[_position++];
    }

    [EngineRoutine(0x00406510)]
    public ReadOnlyMemory<byte> ReadLine()
    {
        var line = new List<byte>();
        Span<byte> chunk = stackalloc byte[LineChunkSize];
        int count = 0;
        int character;
        do
        {
            if (count == LineChunkSize)
            {
                line.AddRange(chunk);
                count = 0;
            }

            character = ReadChar();

            if (character < 0 || character == LineFeed)
            {
                character = 0;
            }

            if (character != CarriageReturn)
            {
                chunk[count++] = (byte)character;
            }
        }
        while (character >= 1);

        line.AddRange(chunk[..(count - 1)]);

        return line.ToArray();
    }

    [EngineRoutine(0x00439bc0)]
    public void WriteText(ReadOnlySpan<byte> text)
    {
        foreach (byte character in text)
        {
            WriteTextByte(character);
        }

        WriteTextByte(0);
    }

    public void SeekBack()
    {
        if (_writer is { Position: > 0 })
        {
            _writer.Position--;
        }
    }

    public void WriteLineFeed() => WriteTextByte(LineFeed);

    public void Dispose() => _writer?.Dispose();

    private static byte[] TranslateText(ReadOnlySpan<byte> data)
    {
        var text = new List<byte>(data.Length);
        for (int i = 0; i < data.Length && data[i] != EndOfText; i++)
        {
            if (data[i] == CarriageReturn && i + 1 < data.Length && data[i + 1] == LineFeed)
                continue;

            text.Add(data[i]);
        }

        return text.ToArray();
    }

    private void WriteTextByte(byte character)
    {
        if (_writer is null)
            return;

        if (character == LineFeed)
        {
            _writer.WriteByte(CarriageReturn);
        }

        _writer.WriteByte(character);
    }
}
