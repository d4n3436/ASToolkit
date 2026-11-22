using System.Buffers;

namespace ASToolkit.Formats;

internal ref struct RangeEncoder
{
    private const uint NormalizeLimit = 0x800000;
    private const uint InitialRange = 0x80000000;
    private const uint CarryThreshold = 0x7f800000;
    private const uint CarryBit = 0x80000000;
    private const uint LowMask = 0x7fffff;
    private const int LowShift = 23;
    private const int TrailerSize = 5;

    private readonly ArrayBufferWriter<byte> _output;
    private uint _low;
    private uint _range;
    private int _pending;
    private byte _buffer;
    private int _count;

    [EngineRoutine(0x00425260)]
    public RangeEncoder(ArrayBufferWriter<byte> output)
    {
        _output = output;
        _low = 0;
        _range = InitialRange;
        _pending = 0;
        _buffer = 0;
        _count = 0;
    }

    [EngineRoutine(0x004252a0)]
    public void Encode(int start, int frequency, int bits)
    {
        Normalize();
        uint step = _range >> bits;
        uint offset = step * (uint)start;
        _low += offset;
        _range = (uint)(start + frequency) >> bits == 0 ? step * (uint)frequency : _range - offset;
    }

    [EngineRoutine(0x004253b0)]
    public int Finish()
    {
        Normalize();
        _count += TrailerSize;
        uint carry = (_low & LowMask) < ((uint)_count >> 1 & LowMask) ? _low >> LowShift : (_low >> LowShift) + 1;
        if (carry < 0x100)
        {
            WriteBuffer(_buffer, 0xff);
        }
        else
        {
            WriteBuffer((byte)(_buffer + 1), 0);
        }

        _output.Write([(byte)carry, (byte)(_count >> 16), (byte)(_count >> 8), (byte)_count]);
        return _count;
    }

    private void Normalize()
    {
        while (_range < NormalizeLimit + 1)
        {
            if (_low < CarryThreshold)
            {
                WriteBuffer(_buffer, 0xff);
                _buffer = (byte)(_low >> LowShift);
            }
            else if ((_low & CarryBit) == 0)
            {
                _pending++;
            }
            else
            {
                WriteBuffer((byte)(_buffer + 1), 0);
                _buffer = (byte)(_low >> LowShift);
            }

            _range <<= 8;
            _low = (_low & LowMask) << 8;
            _count++;
        }
    }

    private void WriteBuffer(byte value, byte filler)
    {
        _output.Write([value]);
        for (; _pending != 0; _pending--)
        {
            _output.Write([filler]);
        }
    }
}
