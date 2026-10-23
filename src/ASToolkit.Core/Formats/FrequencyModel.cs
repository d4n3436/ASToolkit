namespace ASToolkit.Core.Formats;

// Adaptive frequencies of the 256 byte values and the end symbol, rescaled in growing steps as symbols are counted
internal struct FrequencyModel
{
    public const int SymbolCount = 257;
    public const int TotalBits = 12;
    private const int RescaleLimit = 2000;

    private FrequencyTable _cumulative;
    private FrequencyTable _pending;
    private int _rescaleStep;
    private int _nextLeft;
    private int _left;
    private int _increment;

    public void Reset()
    {
        this = default;
        _rescaleStep = (SymbolCount | 0x20) >> 4;
        _cumulative[SymbolCount] = 1 << TotalBits;
        (int share, int remainder) = Math.DivRem(1 << TotalBits, SymbolCount);
        for (int symbol = 0; symbol < SymbolCount; symbol++)
        {
            _pending[symbol] = symbol < remainder ? share + 1 : share;
        }

        Rescale();
    }

    public readonly int GetStart(int symbol) => _cumulative[symbol];

    public readonly int GetFrequency(int symbol) => _cumulative[symbol + 1] - _cumulative[symbol];

    // The last symbol whose cumulative frequency is at most the target
    public readonly int FindSymbol(int target)
    {
        int low = 0;
        int high = SymbolCount;
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

    public void Count(int symbol)
    {
        if (_left <= 0)
        {
            Rescale();
        }

        _left--;
        _pending[symbol] = (_pending[symbol] + _increment) & 0xffff;
    }

    private void Rescale()
    {
        if (_nextLeft != 0)
        {
            _increment++;
            _left = _nextLeft;
            _nextLeft = 0;
            return;
        }

        if (_rescaleStep < RescaleLimit)
        {
            _rescaleStep = Math.Min(_rescaleStep << 1, RescaleLimit);
        }

        int cumulative = _cumulative[SymbolCount];
        int missing = cumulative;
        for (int symbol = SymbolCount - 1; symbol > 0; symbol--)
        {
            int frequency = _pending[symbol];
            cumulative -= frequency;
            _cumulative[symbol] = cumulative;
            frequency = (frequency >> 1) | 1;
            missing -= frequency;
            _pending[symbol] = frequency;
        }

        _pending[0] = (_pending[0] >> 1) | 1;
        missing -= _pending[0];
        _increment = missing / _rescaleStep;
        _nextLeft = missing % _rescaleStep;
        _left = _rescaleStep - _nextLeft;
    }
}
