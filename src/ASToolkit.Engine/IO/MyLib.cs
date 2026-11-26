using System.Globalization;
using System.Text;

namespace ASToolkit.Engine.IO;

internal static class MyLib
{
    public const string DateFormat = "yyyy-MM-dd";
    public const string TimeFormat = "HH:mm:ss";

    private const uint ChecksumPolynomial = 0xEDB88320;
    private const int Base64ChunkSize = 0x39;

    private static readonly uint[] ChecksumTable = CreateChecksumTable();

    private static ReadOnlySpan<byte> Base64Alphabet => "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"u8;

    [EngineRoutine(0x00406130)]
    public static int Sqrt(int value)
    {
        int result = 0;
        for (int bit = 0x40000000; bit != 0; bit >>= 2)
        {
            if (value >= bit + result)
            {
                value -= bit + result;
                result = result >> 1 | bit;
            }
            else
            {
                result >>= 1;
            }
        }

        return result;
    }

    [EngineRoutine(0x00406160)]
    public static uint UpdateChecksum(uint checksum, ReadOnlySpan<byte> text)
    {
        foreach (byte value in text)
        {
            checksum = ChecksumTable[(int)((checksum ^ value) & byte.MaxValue)] ^ checksum >> 8;
        }

        return checksum;
    }

    [EngineRoutine(0x00406630)]
    public static ReadOnlyMemory<byte> LoadFile(IGameFileSystem fileSystem, ReadOnlySpan<byte> name)
    {
        if (name.IsEmpty || !fileSystem.TryRead(Encoding.ASCII.GetString(name), out var data))
            return default;

        return CRuntime.GetString(data);
    }

    [EngineRoutine(0x004066e0)]
    public static byte[] Printf(ReadOnlySpan<byte> format, bool isText, ReadOnlySpan<byte> text, int number) =>
        CRuntime.Format(format, isText, text, number);

    [EngineRoutine(0x004067a0)]
    public static string GetTimeString(TimeProvider time) => time.GetLocalNow().ToString(TimeFormat, CultureInfo.InvariantCulture);

    [EngineRoutine(0x00406850)]
    public static string GetDateString(TimeProvider time) => time.GetLocalNow().ToString(DateFormat, CultureInfo.InvariantCulture);

    extension(string text)
    {
        [EngineRoutine(0x00406c90)]
        public string ReplaceFirst(string from, string to)
        {
            int index = text.IndexOf(from, StringComparison.Ordinal);

            return index < 0 ? text : string.Concat(text.AsSpan(0, index), to, text.AsSpan(index + from.Length));
        }

        public string After(string delimiter)
        {
            int index = text.IndexOf(delimiter, StringComparison.Ordinal);

            return index < 0 ? string.Empty : text[(index + delimiter.Length)..];
        }
    }

    extension(ReadOnlyMemory<byte> text)
    {
        [EngineRoutine(0x00406e50)]
        public ReadOnlyMemory<byte> After(ReadOnlySpan<byte> delimiter)
        {
            var source = CRuntime.GetString(text);
            int index = source.Span.IndexOf(delimiter);

            return index < 0 ? default : source[(index + delimiter.Length)..];
        }
    }

    extension(ReadOnlySpan<byte> text)
    {
        [EngineRoutine(0x00406f20)]
        public ReadOnlySpan<byte> BeforeLast(ReadOnlySpan<byte> delimiter)
        {
            int index = text.LastIndexOf(delimiter);

            return index < 0 ? text : text[..index];
        }

        [EngineRoutine(0x004072e0)]
        public ReadOnlyMemory<byte> ToBase64(int addCode)
        {
            var source = CRuntime.GetString(text);
            byte[] result = new byte[(source.Length + 2) / 3 * 4];
            Span<byte> chunk = stackalloc byte[Base64ChunkSize + 3];
            int written = 0;
            for (int start = 0; start < source.Length; start += Base64ChunkSize)
            {
                int count = Math.Min(Base64ChunkSize, source.Length - start);
                chunk.Clear();
                source.Slice(start, count).CopyTo(chunk);
                for (int i = 0; i < count; i++)
                {
                    chunk[i] = unchecked((byte)(chunk[i] + addCode));
                }

                for (int i = 0; i < count; i += 3)
                {
                    int group = (chunk[i] * 0x100 + chunk[i + 1]) * 0x100 + chunk[i + 2];
                    for (int digit = 3; digit >= 0; digit--)
                    {
                        result[written + digit] = Base64Alphabet[group & 0x3f];
                        group >>= 6;
                    }

                    written += 4;
                }
            }

            for (int padding = (3 - source.Length % 3) % 3; padding > 0; padding--)
            {
                result[written - padding] = (byte)'=';
            }

            return result;
        }

        [EngineRoutine(0x004242e0)]
        public int ParseNumber() =>
            text.Length > 1 && text[1] == 'x' ? CRuntime.ScanInteger(text) : CRuntime.ParseInteger(text);
    }

    private static uint[] CreateChecksumTable()
    {
        uint[] table = new uint[byte.MaxValue + 1];
        for (int i = 0; i < table.Length; i++)
        {
            uint value = (uint)i;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? value >> 1 ^ ChecksumPolynomial : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }
}
