using System.Text;

namespace ASToolkit.Engine.IO;

internal sealed class Profile
{
    private const int BufferSize = 0x7fff;
    private const byte SectionStart = (byte)'[';
    private const byte SectionEnd = (byte)']';
    private const byte Separator = (byte)'=';

    private readonly IGameFileSystem _fileSystem;
    private string _path = "";

    public Profile(IGameFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    private static ReadOnlySpan<byte> SpaceTab => " \t"u8;

    [EngineRoutine(0x00407660)]
    public int Load(string path)
    {
        _path = path;
        return 0;
    }

    [EngineRoutine(0x00407680)]
    public int GetInt(ReadOnlySpan<byte> section, ReadOnlySpan<byte> key, int defaultValue) =>
        TryGetString(section, key, out var value) && !value.IsEmpty ? CRuntime.ParseInteger(value.Span) : defaultValue;

    public ReadOnlyMemory<byte> GetString(ReadOnlySpan<byte> section, ReadOnlySpan<byte> key) =>
        TryGetString(section, key, out var value) ? value : default;

    [EngineRoutine(0x004076a0)]
    public bool TryGetString(ReadOnlySpan<byte> section, ReadOnlySpan<byte> key, out ReadOnlyMemory<byte> value)
    {
        value = default;
        if (!_fileSystem.TryRead(_path, out var data))
            return false;

        bool isInSection = false;
        int start = 0;
        while (start < data.Length)
        {
            int length = data.Span[start..].IndexOfAny("\r\n"u8);
            if (length < 0)
            {
                length = data.Length - start;
            }

            var line = data.Slice(start, length);
            start += length + 1;
            int indent = line.Length - line.Span.TrimStart(SpaceTab).Length;
            line = line[indent..];

            if (!line.IsEmpty && line.Span[0] == SectionStart)
            {
                int close = line.Span.IndexOf(SectionEnd);
                isInSection = Ascii.EqualsIgnoreCase(close < 0 ? line.Span[1..] : line.Span[1..close], section);
                continue;
            }

            int separator = line.Span.IndexOf(Separator);

            if (!isInSection || separator < 0 || !Ascii.EqualsIgnoreCase(line.Span[..separator].TrimEnd(SpaceTab), key))
                continue;

            value = line[(separator + 1)..];
            value = value[^value.Span.TrimStart(SpaceTab).Length..];
            value = value[..value.Span.TrimEnd(SpaceTab).Length];
            if (value.Length >= 2 && "\"'"u8.Contains(value.Span[0]) && value.Span[^1] == value.Span[0])
            {
                value = value[1..^1];
            }

            if (value.Length >= BufferSize)
            {
                value = value[..(BufferSize - 1)];
            }

            return true;
        }

        return false;
    }
}
