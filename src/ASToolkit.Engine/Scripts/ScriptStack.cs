using System.Buffers.Binary;
using System.Buffers.Text;
using ASToolkit.Engine.IO;

namespace ASToolkit.Engine.Scripts;

/// <summary>
/// Represents a value of a script, held in a variable or on the stack.
/// </summary>
public struct ScriptStack
{
    private const byte TextMask = 0x17;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptStack"/> structure as a script value.
    /// </summary>
    /// <param name="flags">The type flags of the value.</param>
    /// <param name="value">The number.</param>
    /// <param name="text">The text.</param>
    public ScriptStack(ScriptStackFlags flags, int value, ReadOnlyMemory<byte> text = default)
    {
        Flags = flags;
        Value = value;
        Text = text;
    }

    /// <summary>
    /// Gets or sets the type flags of the value.
    /// </summary>
    public ScriptStackFlags Flags { get; set; }

    /// <summary>
    /// Gets or sets the number of the value.
    /// </summary>
    public int Value { get; set; }

    /// <summary>
    /// Gets or sets the text of the value.
    /// </summary>
    public ReadOnlyMemory<byte> Text { get; set; }

    /// <summary>
    /// Creates a text value.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The value.</returns>
    public static ScriptStack FromText(ReadOnlyMemory<byte> text) => new(ScriptStackFlags.Text, 0, text);

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
    /// Creates a number value.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>The value.</returns>
    [EngineRoutine(0x004243a0)]
    public static ScriptStack FromNumber(int value) => new(ScriptStackFlags.Number, value);

    /// <summary>
    /// Creates a sprite handle value.
    /// </summary>
    /// <param name="handle">The handle, or 0 if there isn't one.</param>
    /// <returns>The value.</returns>
    [EngineRoutine(0x00439dd0)]
    public static ScriptStack FromHandle(int handle) =>
        new(handle == 0 ? ScriptStackFlags.Number : ScriptStackFlags.Number | ScriptStackFlags.Sprite, handle);

    /// <summary>
    /// Reads the value from a demo or save.
    /// </summary>
    /// <param name="data">The stored state.</param>
    /// <param name="offset">The offset of the value, moved past the value when this method returns.</param>
    [EngineRoutine(0x004244b0)]
    public void Read(ReadOnlyMemory<byte> data, ref int offset)
    {
        Flags = (ScriptStackFlags)data.Span[offset++];
        if (!Flags.HasFlag(ScriptStackFlags.Initialized))
            return;

        if (!Flags.HasFlag(ScriptStackFlags.Text))
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

    /// <summary>
    /// Writes the value to a demo or save stream, as its flags followed by its number or its masked, zero-terminated text.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="number">A four-byte buffer for the number.</param>
    [EngineRoutine(0x00424520)]
    public readonly void Write(Stream stream, Span<byte> number)
    {
        stream.WriteByte((byte)Flags);
        if (!Flags.HasFlag(ScriptStackFlags.Initialized))
            return;

        if (!Flags.HasFlag(ScriptStackFlags.Text))
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
    /// Returns the value as a number, parsing the text when the value is text.
    /// </summary>
    /// <returns>The number.</returns>
    [EngineRoutine(0x00439df0)]
    public readonly int GetValue() => Flags.HasFlag(ScriptStackFlags.Text) ? Text.Span.ParseNumber() : Value;

    /// <summary>
    /// Returns the value as text, with a number written in decimal.
    /// </summary>
    /// <returns>The text.</returns>
    [EngineRoutine(0x00439e30)]
    public readonly ReadOnlyMemory<byte> GetText() => Flags.HasFlag(ScriptStackFlags.Number) ? FormatNumber(Value) : Text;

    [EngineRoutine(0x004245e0)]
    internal void BinarOperator(ScriptOpcode opcode, in ScriptStack right, MyError? error)
    {
        int value = right.GetValue();
        bool areTexts = Flags.HasFlag(ScriptStackFlags.Text) && right.Flags.HasFlag(ScriptStackFlags.Text);
        if (Flags.HasFlag(ScriptStackFlags.Text))
        {
            Value = Text.Span.ParseNumber();
        }

        int current = Value;
        switch (opcode)
        {
            case ScriptOpcode.Divide:
                Value = value == 0 ? 0x0fffffff : current / value;
                break;
            case ScriptOpcode.Modulo:
                Value = current % value;
                break;
            case ScriptOpcode.Add when areTexts:
                byte[] joined = GC.AllocateUninitializedArray<byte>(Text.Length + right.Text.Length);
                Text.Span.CopyTo(joined);
                right.Text.Span.CopyTo(joined.AsSpan(Text.Length));
                Text = joined;
                Flags = ScriptStackFlags.Text;
                return;
            case ScriptOpcode.Add:
                Value = unchecked(current + value);
                break;
            case ScriptOpcode.Subtract:
                Value = unchecked(current - value);
                break;
            case ScriptOpcode.Xor:
                Value = current ^ value;
                break;
            case ScriptOpcode.Or:
                Value = current | value;
                break;
            case ScriptOpcode.And:
                Value = current & value;
                break;
            case ScriptOpcode.Equal:
                Value = (areTexts ? Text.Span.SequenceEqual(right.Text.Span) : current == value) ? 1 : 0;
                break;
            case ScriptOpcode.LogicalOr:
                Value = current == 0 && value == 0 ? 0 : 1;
                break;
            case ScriptOpcode.Greater:
                Value = current > value ? 1 : 0;
                break;
            case ScriptOpcode.Less:
                Value = current < value ? 1 : 0;
                break;
            case ScriptOpcode.GreaterOrEqual:
                Value = current >= value ? 1 : 0;
                break;
            case ScriptOpcode.LessOrEqual:
                Value = current <= value ? 1 : 0;
                break;
            case ScriptOpcode.Multiply:
                Value = unchecked(current * value);
                break;
            case ScriptOpcode.NotEqual:
                Value = (areTexts ? !Text.Span.SequenceEqual(right.Text.Span) : current != value) ? 1 : 0;
                break;
            case ScriptOpcode.LogicalAnd:
                Value = current != 0 && value != 0 ? 1 : 0;
                break;
            case ScriptOpcode.ShiftRight:
                Value = current >> (value & 31);
                break;
            case ScriptOpcode.ShiftLeft:
                Value = current << (value & 31);
                break;
            default:
                error?.Log($"!!!ERROE!!!LOGIC::Unknown Binary command {(int)opcode}");
                break;
        }

        Flags = ScriptStackFlags.Number;
    }
}
