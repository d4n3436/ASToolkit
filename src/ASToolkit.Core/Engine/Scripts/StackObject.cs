using System.Buffers.Binary;
using System.Buffers.Text;

namespace ASToolkit.Core.Engine.Scripts;

/// <summary>
/// Represents a value of a script, held in a variable or on the stack.
/// </summary>
public struct StackObject
{
    private const byte TextMask = 0x17;

    /// <summary>
    /// Initializes a new instance of the <see cref="StackObject"/> structure as a script value.
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
    /// Creates a sprite handle value.
    /// </summary>
    /// <param name="handle">The handle, or 0 if there isn't one.</param>
    /// <returns>The value.</returns>
    [EngineRoutine(0x00439dd0)]
    public static StackObject FromHandle(int handle) =>
        new(handle == 0 ? StackObjectFlags.Number : StackObjectFlags.Number | StackObjectFlags.Sprite, handle, default);

    /// <summary>
    /// Reads a text as a number, as C's <c>sscanf("%i")</c> when its second character is <c>x</c> and as <c>atoi</c> otherwise.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The number, or 0 if <c>sscanf</c> doesn't read a number.</returns>
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
    /// Attempts to parse a number as C's <c>sscanf("%i")</c> does and reports how many bytes it read.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="value">The number, wrapped to 32 bits, when one was read; otherwise, 0.</param>
    /// <param name="length">The number of bytes read, including leading white space, when a number was read; otherwise, 0.</param>
    /// <returns><see langword="true"/> if a number was read; otherwise, <see langword="false"/>.</returns>
    public static bool TryScanInteger(ReadOnlySpan<byte> text, out int value, out int length)
    {
        value = 0;
        length = 0;
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
        length = i;
        return true;
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

    /// <summary>
    /// Returns the value as text, with a number written in decimal.
    /// </summary>
    /// <returns>The text.</returns>
    [EngineRoutine(0x00439e30)]
    public readonly ReadOnlyMemory<byte> GetText() => Flags.HasFlag(StackObjectFlags.Number) ? FormatNumber(Value) : Text;

    /// <summary>
    /// Returns the value as a number, parsing a text.
    /// </summary>
    /// <returns>The number.</returns>
    [EngineRoutine(0x00439df0)]
    public readonly int GetValue() => Flags.HasFlag(StackObjectFlags.Text) ? ParseNumber(Text.Span) : Value;

    /// <summary>
    /// Determines whether the value is nonzero as a number.
    /// </summary>
    /// <returns><see langword="true"/> if the value is nonzero; otherwise, <see langword="false"/>.</returns>
    public readonly bool IsTrue() => GetValue() != 0;

    /// <summary>
    /// Writes the value to a demo or save stream, as its flags followed by its number or its masked, zero-terminated text.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="number">A four-byte buffer for the number.</param>
    [EngineRoutine(0x00424520)]
    public readonly void Write(Stream stream, Span<byte> number)
    {
        stream.WriteByte((byte)Flags);
        if (!Flags.HasFlag(StackObjectFlags.Initialized))
            return;

        if (!Flags.HasFlag(StackObjectFlags.Text))
        {
            BinaryPrimitives.WriteInt32LittleEndian(number, Value);
            stream.Write(number);
            return;
        }

        foreach (byte character in Text.Span)
        {
            stream.WriteByte((byte)(character ^ TextMask));
        }

        stream.WriteByte(0);
    }

    /// <summary>
    /// Reads the value from a demo or save.
    /// </summary>
    /// <param name="data">The stored state.</param>
    /// <param name="offset">The offset of the value, which is moved past the value.</param>
    [EngineRoutine(0x004244b0)]
    public void Read(ReadOnlyMemory<byte> data, ref int offset)
    {
        Flags = (StackObjectFlags)data.Span[offset++];
        if (!Flags.HasFlag(StackObjectFlags.Initialized))
            return;

        if (!Flags.HasFlag(StackObjectFlags.Text))
        {
            Value = BinaryPrimitives.ReadInt32LittleEndian(data.Span[offset..]);
            offset += sizeof(int);
            return;
        }

        byte[] text = Script.ReadString(data, ref offset).ToArray();
        for (int i = 0; i < text.Length; i++)
        {
            text[i] ^= TextMask;
        }

        Text = text;
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
}
