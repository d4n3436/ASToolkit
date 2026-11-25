namespace ASToolkit.Formats;

internal struct FrequencyModel
{
    public const int SymbolCount = 257;
    public const int TotalBits = 12;
    private const int RescaleLimit = 2000;
    private const int RescaleStepBias = 0x20;
    private const int RescaleStepShift = 4;

    private int _symbolCount;
    private int _left;
    private int _nextLeft;
    private int _rescaleStep;
    private int _rescaleLimit;
    private int _increment;
    private FrequencyTable _cumulative;
    private FrequencyTable _pending;

    [EngineRoutine(0x00425690)]
    public FrequencyModel()
    {
        Init(SymbolCount, TotalBits, RescaleLimit);
    }

    [EngineRoutine(0x00425710)]
    private void DoRescale()
    {
        if (_nextLeft != 0)
        {
            _increment++;
            _left = _nextLeft;
            _nextLeft = 0;
            return;
        }

        if (_rescaleStep < _rescaleLimit)
        {
            _rescaleStep = Math.Min(_rescaleStep << 1, _rescaleLimit);
        }

        int cumulative = _cumulative[_symbolCount];
        int missing = cumulative;
        for (int symbol = _symbolCount - 1; symbol > 0; symbol--)
        {
            int frequency = _pending[symbol];
            cumulative -= frequency;
            _cumulative[symbol] = cumulative;
            frequency = (frequency >> 1) | 1;
            missing -= frequency;
            _pending[symbol] = frequency;
        }

        if (cumulative != _pending[0])
            throw new InvalidDataException($"The rescaled frequencies add up to {cumulative} instead of {_pending[0]}.");

        _pending[0] = (_pending[0] >> 1) | 1;
        missing -= _pending[0];
        _increment = missing / _rescaleStep;
        _nextLeft = missing % _rescaleStep;
        _left = _rescaleStep - _nextLeft;
    }

    [EngineRoutine(0x00425820)]
    private void Init(int symbolCount, int totalBits, int rescaleLimit)
    {
        _symbolCount = symbolCount;
        _rescaleLimit = rescaleLimit;
        _cumulative[symbolCount] = 1 << totalBits;
        _cumulative[0] = 0;
        Reset();
    }

    [EngineRoutine(0x004258e0)]
    public void Reset()
    {
        _rescaleStep = (_symbolCount | RescaleStepBias) >> RescaleStepShift;
        _nextLeft = 0;
        (int share, int remainder) = Math.DivRem(_cumulative[_symbolCount], _symbolCount);
        for (int symbol = 0; symbol < _symbolCount; symbol++)
        {
            _pending[symbol] = symbol < remainder ? share + 1 : share;
        }

        DoRescale();
    }

    [EngineRoutine(0x00425970)]
    public readonly void GetFrequency(int symbol, out int frequency, out int start)
    {
        start = _cumulative[symbol];
        frequency = _cumulative[symbol + 1] - start;
    }

    [EngineRoutine(0x004259a0)]
    public readonly int GetSymbol(int target)
    {
        int low = 0;
        int high = _symbolCount;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (_cumulative[middle] <= target)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low - 1;
    }

    [EngineRoutine(0x004259f0)]
    public void Update(int symbol)
    {
        if (_left <= 0)
        {
            DoRescale();
        }

        _left--;
        _pending[symbol] = (_pending[symbol] + _increment) & 0xffff;
    }
}
