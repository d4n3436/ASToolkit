namespace ASToolkit.Engine.IO;

/// <summary>
/// Provides the C runtime routines the engine calls to read numbers from text and to format text.
/// </summary>
public static partial class CRuntime
{
    internal const byte LineFeed = (byte)'\n';
    internal const byte CarriageReturn = (byte)'\r';

    private const int NoDigit = 16;

    internal static ReadOnlySpan<byte> LineEnd => "\r\n"u8;

    /// <summary>
    /// Returns whether a character is white space in the C locale: a space, a tab, a line feed, a vertical tab, a form feed or a carriage return.
    /// </summary>
    /// <param name="character">The character.</param>
    /// <returns><see langword="true"/> if the character is white space; otherwise, <see langword="false"/>.</returns>
    public static bool IsWhiteSpace(byte character) => character == ' ' || character is >= 0x09 and <= 0x0d;

    /// <summary>
    /// Reads a decimal number as C's <c>atoi</c>: white space, an optional sign, then digits up to the first other character.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The number, wrapped to 32 bits, or 0 if the text doesn't start with one.</returns>
    [EngineRoutine(0x0046d39d)]
    public static int ParseInteger(ReadOnlySpan<byte> text)
    {
        int i = 0;
        bool isNegative = ReadSign(text, ref i);
        int total = 0;
        for (; i < text.Length && char.IsAsciiDigit((char)text[i]); i++)
        {
            total = unchecked(total * 10 + (text[i] - '0'));
        }

        return isNegative ? unchecked(-total) : total;
    }

    /// <summary>
    /// Reads a number as C's <c>sscanf("%i")</c>, hexadecimal after <c>0x</c>, octal after <c>0</c> and decimal otherwise.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The number, or 0 if the text doesn't start with a number.</returns>
    public static int ScanInteger(ReadOnlySpan<byte> text) => TryScanInteger(text, out int value) ? value : 0;

    /// <summary>
    /// Attempts to read a number as C's <c>sscanf("%i")</c> does, which fails when the text holds <c>0x</c> without a hex digit.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="value">The number, wrapped to 32 bits, when one was read; otherwise, 0.</param>
    /// <returns><see langword="true"/> if a number was read; otherwise, <see langword="false"/>.</returns>
    public static bool TryScanInteger(ReadOnlySpan<byte> text, out int value) => TryScanInteger(text, out value, out _);

    /// <summary>
    /// Attempts to read a number as C's <c>sscanf("%i")</c> does and reports how many bytes it read.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="value">The number, wrapped to 32 bits, when one was read; otherwise, 0.</param>
    /// <param name="length">The number of bytes read, including leading white space, when a number was read; otherwise, 0.</param>
    /// <returns><see langword="true"/> if a number was read; otherwise, <see langword="false"/>.</returns>
    public static bool TryScanInteger(ReadOnlySpan<byte> text, out int value, out int length)
    {
        value = 0;
        length = 0;
        int i = 0;
        bool isNegative = ReadSign(text, ref i);
        int radix = 10;
        if (i < text.Length && text[i] == '0')
        {
            radix = 8;
            i++;
            if (i < text.Length && text[i] is (byte)'x' or (byte)'X')
            {
                radix = 16;
                i++;
                if (i >= text.Length || GetDigit(text[i]) >= radix)
                    return false;
            }
        }
        else if (i >= text.Length || !char.IsAsciiDigit((char)text[i]))
        {
            return false;
        }

        int total = 0;
        for (; i < text.Length && GetDigit(text[i]) < radix; i++)
        {
            total = unchecked(total * radix + GetDigit(text[i]));
        }

        value = unchecked(isNegative ? -total : total);
        length = i;
        return true;
    }

    internal static ReadOnlySpan<byte> GetString(ReadOnlySpan<byte> text)
    {
        int end = text.IndexOf((byte)0);

        return end < 0 ? text : text[..end];
    }

    internal static ReadOnlyMemory<byte> GetString(ReadOnlyMemory<byte> text)
    {
        int end = text.Span.IndexOf((byte)0);

        return end < 0 ? text : text[..end];
    }

    private static bool ReadSign(ReadOnlySpan<byte> text, ref int index)
    {
        while (index < text.Length && IsWhiteSpace(text[index]))
        {
            index++;
        }

        if (index < text.Length && text[index] is (byte)'-' or (byte)'+')
        {
            return text[index++] == '-';
        }

        return false;
    }

    private static int GetDigit(byte character) => char.IsAsciiDigit((char)character) ? character - '0' :
        char.IsAsciiHexDigit((char)character) ? (character | 0x20) - 'a' + 10 : NoDigit;
}
