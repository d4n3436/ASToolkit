using System.Buffers;
using ASToolkit.Core;

namespace ASToolkit.Formats;

internal sealed class RangeCoder
{
    private const int MinCodedSize = 10;
    private const int EndSymbol = 256;

    [EngineRoutine(0x00425690)]
    public RangeCoder(int bytesPerValue)
    {
        BytesPerValue = bytesPerValue;
        Models = new FrequencyModel[RangeDecoder.ModelCount];
        foreach (ref var model in Models.AsSpan())
        {
            model.Reset();
        }
    }

    public int BytesPerValue { get; }

    public FrequencyModel[] Models { get; }

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
            ref var model = ref Models[context];
            encoder.Encode(model.GetStart(symbol), model.GetFrequency(symbol), FrequencyModel.TotalBits);
            model.Count(symbol);
            context = symbol;
        }

        ref var last = ref Models[context];
        encoder.Encode(last.GetStart(EndSymbol), last.GetFrequency(EndSymbol), FrequencyModel.TotalBits);
        return encoder.Finish();
    }
}
