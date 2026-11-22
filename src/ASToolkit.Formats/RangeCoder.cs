using System.Buffers;

namespace ASToolkit.Formats;

/// <summary>
/// Provides the adaptive range coder that compresses the bodies of RES records, with a frequency model for each value of the preceding byte.
/// </summary>
public sealed class RangeCoder
{
    private const int MinCodedSize = 10;
    private const int EndSymbol = 256;

    private readonly FrequencyModel[] _models;

    /// <summary>
    /// Initializes a new instance of the <see cref="RangeCoder"/> class.
    /// </summary>
    /// <param name="bytesPerValue">The number of bytes of each value of the coded data. With 2, the low bytes of all the values are coded before the high bytes.</param>
    [EngineRoutine(0x00425690)]
    public RangeCoder(int bytesPerValue)
    {
        BytesPerValue = bytesPerValue;
        _models = new FrequencyModel[RangeDecoder.ModelCount];
        foreach (ref var model in _models.AsSpan())
        {
            model.Reset();
        }
    }

    /// <summary>
    /// Gets the number of bytes of each value of the coded data.
    /// </summary>
    public int BytesPerValue { get; }

    /// <summary>
    /// Compresses data and writes it to a buffer. Data shorter than the shortest coded size is copied unchanged.
    /// </summary>
    /// <param name="input">The data to compress.</param>
    /// <param name="output">The buffer the compressed data is written to.</param>
    /// <returns>The number of bytes written.</returns>
    [EngineRoutine(0x00425a40)]
    public int Encode(ReadOnlySpan<byte> input, ArrayBufferWriter<byte> output)
    {
        if (input.IsEmpty)
            return 0;

        if (input.Length < MinCodedSize)
        {
            output.Write(input);
            return input.Length;
        }

        var encoder = new RangeEncoder(output);
        int half = (input.Length + 1) >> 1;
        int context = 0;
        for (int i = 0; i < input.Length; i++)
        {
            int symbol = BytesPerValue == 2 ? i < half ? input[2 * i] : input[2 * (i - half) + 1] : input[i];
            ref var model = ref _models[context];
            encoder.Encode(model.GetStart(symbol), model.GetFrequency(symbol), FrequencyModel.TotalBits);
            model.Count(symbol);
            context = symbol;
        }

        ref var last = ref _models[context];
        encoder.Encode(last.GetStart(EndSymbol), last.GetFrequency(EndSymbol), FrequencyModel.TotalBits);
        return encoder.Finish();
    }
}
