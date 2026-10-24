using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Scripting;

/// <summary>
/// Represents the action stacks and the name a placed sprite carries in its payload.
/// </summary>
public readonly struct SpriteProgram
{
    private const int MaxStackCount = 4;

    private static ReadOnlySpan<byte> NameSpaces => " \t\r\n"u8;

    private readonly ReadOnlyMemory<byte> _payload;
    private readonly int _firstStack;
    private readonly int _nameStart;
    private readonly int _nameLength;

    private SpriteProgram(ReadOnlyMemory<byte> payload, int firstStack, int stackCount, int nameStart, int nameLength)
    {
        _payload = payload;
        _firstStack = firstStack;
        StackCount = stackCount;
        _nameStart = nameStart;
        _nameLength = nameLength;
    }

    /// <summary>
    /// Gets the number of action stacks.
    /// </summary>
    public int StackCount { get; }

    /// <summary>
    /// Gets a value indicating whether the sprite has a name.
    /// </summary>
    public bool HasName => _nameLength > 0;

    /// <summary>
    /// Reads the program of a placed sprite, or returns false when the payload holds no program.
    /// </summary>
    /// <param name="payload">The payload bytes.</param>
    /// <param name="sequel">A value indicating whether the payload follows the Alien Shooter 2 layout.</param>
    /// <param name="program">The program.</param>
    public static bool TryParse(ReadOnlyMemory<byte> payload, bool sequel, out SpriteProgram program) =>
        sequel ? TryParseSequel(payload, out program) : TryParseOriginal(payload, out program);

    /// <summary>
    /// Returns the entries of an action stack in file order.
    /// </summary>
    /// <param name="index">The stack index.</param>
    public ReadOnlySpan<MapAction> GetStack(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, StackCount);
        var payload = _payload.Span;
        int position = _firstStack;
        for (int stack = 0; stack < index; stack++)
        {
            position += sizeof(int) + MemoryMarshal.Read<int>(payload[position..]) * Unsafe.SizeOf<MapAction>();
        }

        int count = MemoryMarshal.Read<int>(payload[position..]);
        return MemoryMarshal.Cast<byte, MapAction>(payload.Slice(position + sizeof(int), count * Unsafe.SizeOf<MapAction>()));
    }

    /// <summary>
    /// Returns the name of the sprite, or an empty string.
    /// </summary>
    public string GetName() => _nameLength > 0 ? GameTextEncoding.Cyrillic.GetString(_payload.Span.Slice(_nameStart, _nameLength)) : "";

    private static bool TryParseOriginal(ReadOnlyMemory<byte> payload, out SpriteProgram program)
    {
        program = default;
        int position = 0;
        if (!TrySkipStack(payload.Span, ref position, out bool empty) || empty)
        {
            return false;
        }

        program = new SpriteProgram(payload, 0, 1, 0, 0);
        return true;
    }

    private static bool TryParseSequel(ReadOnlyMemory<byte> payload, out SpriteProgram program)
    {
        program = default;
        var span = payload.Span;
        if (span.Length < 2 * sizeof(int))
        {
            return false;
        }

        int stackCount = MemoryMarshal.Read<int>(span[sizeof(int)..]);
        if (stackCount is < 0 or > MaxStackCount)
        {
            return false;
        }

        int position = 2 * sizeof(int);
        bool allEmpty = true;
        for (int stack = 0; stack < stackCount; stack++)
        {
            if (!TrySkipStack(span, ref position, out bool empty))
            {
                return false;
            }

            allEmpty &= empty;
        }

        (int nameStart, int nameLength) = FindName(span, position);
        if (allEmpty && nameLength == 0)
        {
            return false;
        }

        program = new SpriteProgram(payload, 2 * sizeof(int), stackCount, nameStart, nameLength);
        return true;
    }

    private static bool TrySkipStack(ReadOnlySpan<byte> payload, ref int position, out bool empty)
    {
        empty = true;
        if (position + sizeof(int) > payload.Length)
        {
            return false;
        }

        int count = MemoryMarshal.Read<int>(payload[position..]);
        position += sizeof(int);
        if (count < 0 || position + count * Unsafe.SizeOf<MapAction>() > payload.Length)
        {
            return false;
        }

        position += count * Unsafe.SizeOf<MapAction>();
        empty = count == 0;
        return true;
    }

    private static (int Start, int Length) FindName(ReadOnlySpan<byte> payload, int position)
    {
        if (position + sizeof(int) > payload.Length)
        {
            return (0, 0);
        }

        int count = MemoryMarshal.Read<int>(payload[position..]);
        long start = position + sizeof(int) + (long)count * sizeof(int);
        if (count < 0 || start >= payload.Length)
        {
            return (0, 0);
        }

        var text = payload[(int)start..];
        int end = text.IndexOf((byte)0);
        text = end < 0 ? text : text[..end];
        int length = text.Length;
        text = text.TrimStart(NameSpaces);
        return ((int)start + length - text.Length, text.TrimEnd(NameSpaces).Length);
    }
}
