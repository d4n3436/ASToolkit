using System.Text;

namespace ASToolkit.Engine.IO;

/// <summary>
/// Provides the C runtime routines the engine uses to convert, read and format text.
/// </summary>
public static partial class CRuntime
{
    internal const byte LineFeed = (byte)'\n';
    internal const byte CarriageReturn = (byte)'\r';

    private const int NoDigit = 16;

    internal static ReadOnlySpan<byte> LineEnd => "\r\n"u8;

    /// <summary>
    /// Determines whether a character is a space, a tab, a line feed, a vertical tab, a form feed or a carriage return (<c>isspace</c>).
    /// </summary>
    /// <param name="character">The character.</param>
    /// <returns><see langword="true"/> if the character is white space; otherwise, <see langword="false"/>.</returns>
    public static bool IsWhiteSpace(byte character) => character == ' ' || character is >= 0x09 and <= 0x0d;

    /// <summary>
    /// Converts the uppercase ASCII letters of a text to lowercase in place (<c>_strlwr</c>).
    /// </summary>
    /// <param name="text">The text.</param>
    [EngineRoutine(0x00479c60)]
    public static void ToLowerInPlace(Span<byte> text)
    {
        while (!text.IsEmpty)
        {
            Ascii.ToLowerInPlace(text, out int converted);
            text = text[Math.Min(converted + 1, text.Length)..];
        }
    }

    /// <summary>
    /// Converts the uppercase ASCII letters of a text to lowercase in place (<c>_strlwr</c>).
    /// </summary>
    /// <param name="text">The text.</param>
    [EngineRoutine(0x00479c60)]
    public static void ToLowerInPlace(Span<char> text)
    {
        while (!text.IsEmpty)
        {
            Ascii.ToLowerInPlace(text, out int converted);
            text = text[Math.Min(converted + 1, text.Length)..];
        }
    }

    /// <summary>
    /// Converts the lowercase ASCII letters of a text to uppercase in place (<c>_strupr</c>).
    /// </summary>
    /// <param name="text">The text.</param>
    [EngineRoutine(0x00479cfe)]
    public static void ToUpperInPlace(Span<byte> text)
    {
        while (!text.IsEmpty)
        {
            Ascii.ToUpperInPlace(text, out int converted);
            text = text[Math.Min(converted + 1, text.Length)..];
        }
    }

    /// <summary>
    /// Reads a decimal number after any leading white space and an optional sign (<c>atoi</c>).
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
    /// Reads a number that is hexadecimal after <c>0x</c>, octal after <c>0</c> and decimal otherwise (<c>sscanf("%i")</c>).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The number, or 0 if the text doesn't start with a number.</returns>
    public static int ScanInteger(ReadOnlySpan<byte> text) => TryScanInteger(text, out int value) ? value : 0;

    /// <summary>
    /// Attempts to read a number that is hexadecimal after <c>0x</c>, octal after <c>0</c> and decimal otherwise (<c>sscanf("%i")</c>).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="value">When this method returns, contains the number, wrapped to 32 bits, if one was read; otherwise, 0.</param>
    /// <returns><see langword="true"/> if a number was read; otherwise, <see langword="false"/>.</returns>
    public static bool TryScanInteger(ReadOnlySpan<byte> text, out int value) => TryScanInteger(text, out value, out _);

    /// <summary>
    /// Attempts to read a number that is hexadecimal after <c>0x</c>, octal after <c>0</c> and decimal otherwise, and the number of bytes it takes up (<c>sscanf("%i")</c>).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="value">When this method returns, contains the number, wrapped to 32 bits, if one was read; otherwise, 0.</param>
    /// <param name="length">When this method returns, contains the number of bytes read, including leading white space, if a number was read; otherwise, 0.</param>
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
