using System.Text;

namespace ASToolkit.Engine.IO;

internal static class ProfileFile
{
    private const int BufferSize = 0x7fff;
    private const byte SectionStart = (byte)'[';
    private const byte SectionEnd = (byte)']';
    private const byte Separator = (byte)'=';

    private static ReadOnlySpan<byte> SpaceTab => " \t"u8;

    private static ReadOnlySpan<byte> CrLf => "\r\n"u8;

    private static ReadOnlySpan<byte> Quotes => "\"'"u8;

    public static ReadOnlyMemory<byte> GetString(IGameFileSystem fileSystem, string path, ReadOnlySpan<byte> section, ReadOnlySpan<byte> key) =>
        TryGetString(fileSystem, path, section, key, out var value) ? value : ReadOnlyMemory<byte>.Empty;

    [EngineRoutine(0x00407680)]
    public static int GetInteger(IGameFileSystem fileSystem, string path, ReadOnlySpan<byte> section, ReadOnlySpan<byte> key, int defaultValue) =>
        TryGetString(fileSystem, path, section, key, out var value) && !value.IsEmpty ? CRuntime.ParseInteger(value.Span) : defaultValue;

    [EngineRoutine(0x004076a0)]
    public static bool TryGetString(IGameFileSystem fileSystem, string path, ReadOnlySpan<byte> section, ReadOnlySpan<byte> key, out ReadOnlyMemory<byte> value)
    {
        value = ReadOnlyMemory<byte>.Empty;
        if (!fileSystem.TryRead(path, out var data))
            return false;

        bool isInSection = false;
        int start = 0;
        while (start < data.Length)
        {
            int length = data.Span[start..].IndexOfAny(CrLf);
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
            if (value.Length >= 2 && Quotes.Contains(value.Span[0]) && value.Span[^1] == value.Span[0])
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
