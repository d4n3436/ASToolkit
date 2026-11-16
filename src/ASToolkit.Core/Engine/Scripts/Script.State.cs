using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ASToolkit.Core.Engine.Scripts;

public sealed partial class Script
{
    private const byte TextMask = 0x17;

    /// <summary>
    /// Writes the variables and names of the script as demos and saves store them.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    [EngineRoutine(0x0041fa30)]
    public void WriteState(Stream stream)
    {
        Span<byte> number = stackalloc byte[sizeof(int)];
        for (int i = 0; i < _count; i++)
        {
            WriteVariable(stream, _slots[i], number);
        }

        BinaryPrimitives.WriteInt32LittleEndian(number, _objectCount);
        stream.Write(number);
        for (int i = 0; i < _objectCount; i++)
        {
            ref readonly var entry = ref _objects[i];
            stream.Write(entry.Name.Span);
            stream.WriteByte(0);
            var record = new ScriptNameRecord(entry.Kind, entry.Address, entry.FirstVariable, entry.Count);
            stream.Write(MemoryMarshal.AsBytes(new ReadOnlySpan<ScriptNameRecord>(in record)));
        }

        for (int i = 0; i < _objectCount; i++)
        {
            stream.Write(_objects[i].Text.Span);
            stream.WriteByte(0);
        }
    }

    [EngineRoutine(0x00424520)]
    private static void WriteVariable(Stream stream, in StackObject slot, Span<byte> number)
    {
        stream.WriteByte((byte)slot.Flags);
        if (!slot.Flags.HasFlag(StackObjectFlags.Initialized))
            return;

        if (!slot.Flags.HasFlag(StackObjectFlags.Text))
        {
            BinaryPrimitives.WriteInt32LittleEndian(number, slot.Value);
            stream.Write(number);
            return;
        }

        foreach (byte character in slot.Text.Span)
        {
            stream.WriteByte((byte)(character ^ TextMask));
        }

        stream.WriteByte(0);
    }

    /// <summary>
    /// Reads the variables and names of the script as demos and saves store them, in slot and name order.
    /// </summary>
    /// <param name="data">The stored state.</param>
    /// <returns>The number of bytes read.</returns>
    [EngineRoutine(0x0041fb20)]
    public int ReadState(ReadOnlyMemory<byte> data)
    {
        int offset = 0;
        for (int i = 0; i < _count; i++)
        {
            ReadVariable(data, ref offset, ref _slots[i]);
        }

        _objectCount = BinaryPrimitives.ReadInt32LittleEndian(data.Span[offset..]);
        offset += sizeof(int);
        EnsureObjectCapacity(_objectCount);
        for (int i = 0; i < _objectCount; i++)
        {
            ref var entry = ref _objects[i];
            entry.Name = ReadString(data, ref offset);
            var record = MemoryMarshal.Read<ScriptNameRecord>(data.Span[offset..]);
            entry.Kind = record.Kind;
            entry.Address = record.Address;
            entry.FirstVariable = record.FirstVariable;
            entry.Count = record.Count;
            offset += Unsafe.SizeOf<ScriptNameRecord>();
        }

        for (int i = 0; i < _objectCount; i++)
        {
            _objects[i].Text = ReadString(data, ref offset);
        }

        return offset;
    }

    [EngineRoutine(0x004244b0)]
    private static void ReadVariable(ReadOnlyMemory<byte> data, ref int offset, ref StackObject slot)
    {
        slot.Flags = (StackObjectFlags)data.Span[offset++];
        if (!slot.Flags.HasFlag(StackObjectFlags.Initialized))
            return;

        if (!slot.Flags.HasFlag(StackObjectFlags.Text))
        {
            slot.Value = BinaryPrimitives.ReadInt32LittleEndian(data.Span[offset..]);
            offset += sizeof(int);
            return;
        }

        byte[] text = ReadString(data, ref offset).ToArray();
        for (int i = 0; i < text.Length; i++)
        {
            text[i] ^= TextMask;
        }

        slot.Text = text;
    }

    [EngineRoutine(0x00406590)]
    private static ReadOnlyMemory<byte> ReadString(ReadOnlyMemory<byte> data, ref int offset)
    {
        var rest = data.Span[offset..];
        int length = rest.IndexOfAny((byte)0, (byte)'\n');
        if (length < 0)
        {
            length = rest.Length;
        }

        var text = data.Slice(offset, length);
        offset += Math.Min(length + 1, rest.Length);
        if (!text.Span.Contains((byte)'\r'))
            return text;

        byte[] kept = new byte[length - text.Span.Count((byte)'\r')];
        int count = 0;
        foreach (byte character in text.Span)
        {
            if (character != '\r')
            {
                kept[count++] = character;
            }
        }

        return kept;
    }

    [EngineRoutine(0x00425080)]
    private void EnsureObjectCapacity(int count)
    {
        if (_objects.Length < count)
        {
            Array.Resize(ref _objects, count);
        }
    }
}
