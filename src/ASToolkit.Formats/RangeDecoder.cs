namespace ASToolkit.Formats;

internal ref struct RangeDecoder
{
    public const int ModelCount = 256;
    private const int EndSymbol = 256;
    private const int MinCodedSize = 10;
    private const uint NormalizeLimit = 0x800000;
    private const uint InitialRange = 0x80;
    private const int EndOfStream = -1;

    private readonly ReadOnlySpan<byte> _input;
    private int _position;
    private byte _buffer;
    private uint _low;
    private uint _range;
    private uint _step;

    private RangeDecoder(ReadOnlySpan<byte> input)
    {
        _input = input;
    }

    public static int Decode(ReadOnlySpan<byte> input, Span<byte> output, Span<FrequencyModel> models, int planes, out int used)
    {
        if (output.IsEmpty)
        {
            used = 0;
            return 0;
        }

        if (output.Length < MinCodedSize)
        {
            used = Math.Min(input.Length, output.Length);
            input[..used].CopyTo(output);
            return used;
        }

        var decoder = new RangeDecoder(input);
        if (!decoder.TryStart())
        {
            used = decoder._position;
            return 0;
        }

        int half = (output.Length + 1) >> 1;
        int count = 0;
        int context = 0;
        while (true)
        {
            ref var model = ref models[context];
            int symbol = model.GetSymbol(decoder.GetTarget(FrequencyModel.TotalBits));
            model.GetFrequency(symbol, out int frequency, out int start);
            if (symbol == EndSymbol || count >= output.Length)
            {
                if (symbol != EndSymbol)
                {
                    using var error = Console.OpenStandardError();
                    error.Write("!!!ERROR!!! decode"u8);
                }

                model.GetFrequency(EndSymbol, out int endFrequency, out int endStart);
                decoder.Consume(endFrequency, endStart, 1 << FrequencyModel.TotalBits);
                decoder.Normalize();
                break;
            }

            output[planes != 2 ? count : count < half ? 2 * count : 2 * (count - half) + 1] = (byte)symbol;
            count++;
            decoder.Consume(frequency, start, 1 << FrequencyModel.TotalBits);
            model.Update(symbol);
            context = symbol;
        }

        used = decoder._position;
        return count;
    }

    private bool TryStart()
    {
        int first = ReadByte();
        if (first == EndOfStream)
            return false;

        _buffer = (byte)ReadByte();
        _low = (uint)_buffer >> 1;
        _range = InitialRange;
        return first == 0;
    }

    private int ReadByte()
    {
        if (_position >= _input.Length)
            return EndOfStream;

        return _input[_position++];
    }

    private void Normalize()
    {
        while (_range <= NormalizeLimit)
        {
            _low = ((_low << 1) | (uint)(_buffer & 1)) << 7;
            _buffer = (byte)ReadByte();
            _low |= (uint)_buffer >> 1;
            _range <<= 8;
        }
    }

    private int GetTarget(int bits)
    {
        Normalize();
        _step = _range >> bits;
        uint target = _low / _step;
        return target >> bits != 0 ? (1 << bits) - 1 : (int)target;
    }

    private void Consume(int frequency, int start, int total)
    {
        uint offset = _step * (uint)start;
        _low -= offset;
        _range = start + frequency < total ? _step * (uint)frequency : _range - offset;
    }
}
