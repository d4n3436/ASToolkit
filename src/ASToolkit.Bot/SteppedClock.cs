namespace ASToolkit.Bot;

/// <summary>
/// Provides a clock that changes only when it is set, at scheduled loading-screen redraws, and while the game waits for it to change.
/// </summary>
public sealed class SteppedClock : TimeProvider
{
    private const long RedrawStep = 1 << 16;
    private const long MillisecondsPerSecond = 1000;
    private const int WaitReads = 1 << 16;

    private readonly TimeProvider _inner;
    private int[] _redraws = [];
    private int _nextRedraw;
    private int _read;
    private long _time;
    private long _lastRead = -1;
    private int _repeatedReads;

    /// <summary>
    /// Initializes a new instance of the <see cref="SteppedClock"/> class.
    /// </summary>
    /// <param name="inner">The time provider that gives the date and the time zone, or <see langword="null"/> to use <see cref="TimeProvider.System"/>.</param>
    public SteppedClock(TimeProvider? inner = null)
    {
        _inner = inner ?? System;
    }

    /// <inheritdoc/>
    public override long TimestampFrequency => MillisecondsPerSecond;

    /// <inheritdoc/>
    public override TimeZoneInfo LocalTimeZone => _inner.LocalTimeZone;

    /// <summary>
    /// Sets the clock time.
    /// </summary>
    /// <param name="milliseconds">The time in milliseconds.</param>
    public void Set(uint milliseconds) => _time = milliseconds;

    /// <summary>
    /// Sets the clock reads at which the loading screen redraws, counted from the next read.
    /// </summary>
    /// <param name="redraws">The zero-based indexes of those reads.</param>
    public void Schedule(IEnumerable<int> redraws)
    {
        _redraws = redraws.Order().ToArray();
        _nextRedraw = 0;
        _read = 0;
    }

    /// <inheritdoc/>
    public override long GetTimestamp()
    {
        while (_nextRedraw < _redraws.Length && _redraws[_nextRedraw] == _read)
        {
            _time += RedrawStep;
            _nextRedraw++;
        }

        _read++;

        if (_time != _lastRead)
        {
            _repeatedReads = 0;
        }
        else if (++_repeatedReads >= WaitReads)
        {
            _time++;
            _repeatedReads = 0;
        }

        _lastRead = _time;

        return _time & uint.MaxValue;
    }

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => _inner.GetUtcNow();
}
