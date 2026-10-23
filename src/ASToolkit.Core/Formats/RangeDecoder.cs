namespace ASToolkit.Core.Formats;

// Carry-less range decoder of the range coded sprite surfaces, with one frequency model per previous byte
internal ref struct RangeDecoder
{
    public const int ModelCount = 256;
    private const int EndSymbol = 256;
    private const int MinCodedSize = 10;
    private const uint NormalizeLimit = 0x800000;
    private const uint InitialRange = 0x80;

    private readonly ReadOnlySpan<byte> _input;
    private int _position;
    private uint _buffer;
    private uint _low;
    private uint _range;
    private uint _step;

    private RangeDecoder(ReadOnlySpan<byte> input)
    {
        _input = input;
        ReadByte();
        _buffer = ReadByte();
        _low = _buffer >> 1;
        _range = InitialRange;
    }

    // Fills the output with the low bytes of the 16-bit values first and the high bytes second, and fails when the stream ends early
    public static bool TryDecode(ReadOnlySpan<byte> input, Span<byte> output, Span<FrequencyModel> models, out int used)
    {
        if (output.Length < MinCodedSize)
        {
            used = output.Length;
            return input.Length >= used && input[..used].TryCopyTo(output);
        }

        var decoder = new RangeDecoder(input);
        int half = (output.Length + 1) >> 1;
        int count = 0;
        int context = 0;
        while (true)
        {
            ref var model = ref models[context];
            int symbol = model.FindSymbol(decoder.GetTarget(FrequencyModel.TotalBits));
            int frequency = model.GetFrequency(symbol);
            int start = model.GetStart(symbol);
            if (symbol == EndSymbol)
            {
                decoder.Consume(frequency, start, 1 << FrequencyModel.TotalBits);
                decoder.Normalize();
                break;
            }

            if (count >= output.Length)
            {
                break;
            }

            output[count < half ? 2 * count : 2 * (count - half) + 1] = (byte)symbol;
            count++;
            decoder.Consume(frequency, start, 1 << FrequencyModel.TotalBits);
            model.Count(symbol);
            context = symbol;
        }

        used = decoder._position;
        return count == output.Length;
    }

    private uint ReadByte()
    {
        uint value = _position < _input.Length ? _input[_position] : 0u;
        _position++;
        return value;
    }

    private void Normalize()
    {
        while (_range <= NormalizeLimit)
        {
            _low = ((_low << 1) | (_buffer & 1)) << 7;
            _buffer = ReadByte();
            _low |= _buffer >> 1;
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
