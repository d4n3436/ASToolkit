using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ASToolkit.Engine.IO;
using ASToolkit.Core;

namespace ASToolkit.Engine.Scripts;

public sealed partial class Script
{
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
            _slots[i].Write(stream, number);
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
            _slots[i].Read(data, ref offset);
        }

        _objectCount = BinaryPrimitives.ReadInt32LittleEndian(data.Span[offset..]);
        offset += sizeof(int);
        EnsureObjectCapacity(_objectCount);
        for (int i = 0; i < _objectCount; i++)
        {
            ref var entry = ref _objects[i];
            entry.Name = ReadString(data, ref offset);
            ref readonly var record = ref MemoryMarshal.AsRef<ScriptNameRecord>(data.Span[offset..]);
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

    internal static ReadOnlyMemory<byte> ReadString(ReadOnlyMemory<byte> data, ref int offset)
    {
        int start = offset;
        var text = Resource.ReadText(data.Span, ref offset);
        return text.Overlaps(data.Span) ? data.Slice(start, text.Length) : text.ToArray();
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
