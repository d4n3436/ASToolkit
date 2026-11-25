using System.Text;

namespace ASToolkit.Engine.IO;

/// <summary>
/// Provides C runtime routines that convert, read and format text.
/// </summary>
public static partial class CRuntime
{
    internal const byte LineFeed = (byte)'\n';
    internal const byte CarriageReturn = (byte)'\r';
    private const int NoDigit = 16;

    internal static ReadOnlySpan<byte> LineEnd => "\r\n"u8;

    private static ReadOnlySpan<byte> HighSpaces => [0xcf, 0xdb, 0xe4, 0xe7, 0xe9, 0xef, 0xf1, 0xf3, 0xf4, 0xf7, 0xf9, 0xfb, 0xfc, 0xfd, 0xfe];

    private static ReadOnlySpan<byte> HighDigits =>
    [
        0xcb, 0xcc, 0xcf, 0xd8, 0xdb, 0xe4, 0xe8, 0xe9, 0xea, 0xeb, 0xec,
        0xed, 0xee, 0xef, 0xf0, 0xf2, 0xf3, 0xf4, 0xf7, 0xf8, 0xf9, 0xfa
    ];

    private static ReadOnlySpan<byte> HighLetters =>
    [
        0xcb, 0xcc, 0xcd, 0xcf, 0xd6, 0xd7, 0xd8, 0xdb, 0xe3, 0xe4, 0xe7, 0xe8, 0xe9, 0xea, 0xec,
        0xed, 0xee, 0xef, 0xf0, 0xf1, 0xf2, 0xf3, 0xf4, 0xf7, 0xf8, 0xf9, 0xfa, 0xfb, 0xfd
    ];

    private static ReadOnlySpan<byte> HighLettersOrDigits =>
    [
        0xcb, 0xcc, 0xcd, 0xcf, 0xd6, 0xd7, 0xd8, 0xdb, 0xe3, 0xe4, 0xe7, 0xe8, 0xe9, 0xea, 0xeb,
        0xec, 0xed, 0xee, 0xef, 0xf0, 0xf1, 0xf2, 0xf3, 0xf4, 0xf7, 0xf8, 0xf9, 0xfa, 0xfb, 0xfd
    ];

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

    [EngineRoutine(0x0046d3fe)]
    internal static bool IsSpace(byte character) => IsWhiteSpace(character) || character > sbyte.MaxValue && HighSpaces.Contains(character);

    [EngineRoutine(0x0046d3d6)]
    internal static bool IsDigit(byte character) => char.IsAsciiDigit((char)character) || character > sbyte.MaxValue && HighDigits.Contains(character);

    [EngineRoutine(0x0046d3a8)]
    internal static bool IsAlpha(byte character) => char.IsAsciiLetter((char)character) || character > sbyte.MaxValue && HighLetters.Contains(character);

    [EngineRoutine(0x0046d426)]
    internal static bool IsAlphanumeric(byte character) => char.IsAsciiLetterOrDigit((char)character) || character > sbyte.MaxValue && HighLettersOrDigits.Contains(character);

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
