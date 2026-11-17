using System.Buffers.Text;

namespace ASToolkit.Core.Engine.Scripts;

/// <summary>
/// Represents a value of a script, held in a variable or on the stack.
/// </summary>
public struct StackObject
{
    /// <summary>
    /// Creates a script value.
    /// </summary>
    /// <param name="flags">The type flags of the value.</param>
    /// <param name="value">The number.</param>
    /// <param name="text">The text.</param>
    public StackObject(StackObjectFlags flags, int value, ReadOnlyMemory<byte> text)
    {
        Flags = flags;
        Value = value;
        Text = text;
    }

    /// <summary>
    /// Gets or sets the type flags of the value.
    /// </summary>
    public StackObjectFlags Flags { get; set; }

    /// <summary>
    /// Gets or sets the number of the value.
    /// </summary>
    public int Value { get; set; }

    /// <summary>
    /// Gets or sets the text of the value.
    /// </summary>
    public ReadOnlyMemory<byte> Text { get; set; }

    /// <summary>
    /// Creates a number value.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>The value.</returns>
    public static StackObject FromNumber(int value) => new(StackObjectFlags.Number, value, default);

    /// <summary>
    /// Creates a text value.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The value.</returns>
    public static StackObject FromText(ReadOnlyMemory<byte> text) => new(StackObjectFlags.Text, 0, text);

    /// <summary>
    /// Returns the value as a number, parsing a text.
    /// </summary>
    /// <returns>The number.</returns>
    public readonly int GetValue() => Flags.HasFlag(StackObjectFlags.Text) ? ParseNumber(Text.Span) : Value;

    /// <summary>
    /// Returns whether the value is nonzero as a number.
    /// </summary>
    /// <returns><see langword="true"/> when the value is nonzero; otherwise, <see langword="false"/>.</returns>
    public readonly bool IsTrue() => GetValue() != 0;

    /// <summary>
    /// Reads a text as a number, as C's <c>sscanf("%i")</c> when its second character is <c>x</c> and as <c>atoi</c> otherwise.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The number, or 0 when <c>sscanf</c> reads none.</returns>
    [EngineRoutine(0x004242e0)]
    public static int ParseNumber(ReadOnlySpan<byte> text)
    {
        if (text.Length > 1 && text[1] == 'x')
            return ScanInteger(text);

        int i = SkipSpace(text, 0);
        bool negative = false;
        if (i < text.Length && (text[i] == '-' || text[i] == '+'))
        {
            negative = text[i] == '-';
            i++;
        }

        int total = 0;
        for (; i < text.Length && char.IsAsciiDigit((char)text[i]); i++)
        {
            total = unchecked(total * 10 + (text[i] - '0'));
        }

        return negative ? unchecked(-total) : total;
    }

    /// <summary>
    /// Reads a number as C's <c>sscanf("%i")</c>, hexadecimal after <c>0x</c>, octal after <c>0</c> and decimal otherwise.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The number, or 0 when the text starts with none.</returns>
    public static int ScanInteger(ReadOnlySpan<byte> text) => TryScanInteger(text, out int value) ? value : 0;

    /// <summary>
    /// Reads a number as C's <c>sscanf("%i")</c>, returning false when the text starts with none, as for <c>0x</c> without a digit.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="value">The number, wrapped to 32 bits.</param>
    /// <returns><see langword="true"/> when a number was read; otherwise, <see langword="false"/>.</returns>
    public static bool TryScanInteger(ReadOnlySpan<byte> text, out int value)
    {
        value = 0;
        int i = SkipSpace(text, 0);
        bool negative = false;
        if (i < text.Length && (text[i] == '-' || text[i] == '+'))
        {
            negative = text[i] == '-';
            i++;
        }

        int radix = 10;
        if (i < text.Length && text[i] == '0')
        {
            radix = 8;
            i++;
            if (i < text.Length && (text[i] | 0x20) == 'x')
            {
                radix = 16;
                i++;
                if (i >= text.Length || GetDigit(text[i]) >= radix)
                    return false;
            }
        }
        else if (i >= text.Length || !char.IsAsciiDigit((char)text[i]))
            return false;

        int total = 0;
        for (; i < text.Length && GetDigit(text[i]) < radix; i++)
        {
            total = unchecked(total * radix + GetDigit(text[i]));
        }

        value = unchecked(negative ? -total : total);
        return true;
    }

    private static int GetDigit(byte character) => char.IsAsciiDigit((char)character) ? character - '0' :
        char.IsAsciiHexDigit((char)character) ? (character | 0x20) - 'a' + 10 : 16;

    private static int SkipSpace(ReadOnlySpan<byte> text, int index)
    {
        while (index < text.Length && (text[index] == ' ' || text[index] is >= 0x09 and <= 0x0d))
        {
            index++;
        }

        return index;
    }

    /// <summary>
    /// Writes a number as decimal text.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>The text.</returns>
    [EngineRoutine(0x00424310)]
    public static ReadOnlyMemory<byte> FormatNumber(int value)
    {
        byte[] text = new byte[11];
        Utf8Formatter.TryFormat(value, text, out int length);

        return text.AsMemory()[..length];
    }
}
