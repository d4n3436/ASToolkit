namespace ASToolkit.Bot;

/// <summary>
/// Provides a clock that stands still except at the reads where the loading screen should redraw.
/// </summary>
/// <remarks>
/// The loading screen redraws when a frame's length has passed since its last redraw.
/// This clock moves forward by more than any frame length at each scheduled read, so that the redraws match a recorded run.
/// </remarks>
public sealed class LoadingRedrawClock : TimeProvider
{
    private const long RedrawStep = 1 << 16;
    private const long MillisecondsPerSecond = 1000;

    private readonly TimeProvider _inner;
    private HashSet<int> _redraws = [];
    private int _read;
    private long _time;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoadingRedrawClock"/> class.
    /// </summary>
    /// <param name="inner">The time provider that gives the date and the time zone, or <see langword="null"/> to use the system's.</param>
    public LoadingRedrawClock(TimeProvider? inner = null)
    {
        _inner = inner ?? System;
    }

    /// <inheritdoc/>
    public override long TimestampFrequency => MillisecondsPerSecond;

    /// <inheritdoc/>
    public override TimeZoneInfo LocalTimeZone => _inner.LocalTimeZone;

    /// <summary>
    /// Sets the reads at which the loading screen redraws, counted from the next read.
    /// </summary>
    /// <param name="redraws">The indexes of the reads that redraw.</param>
    public void Schedule(IEnumerable<int> redraws)
    {
        _redraws = new HashSet<int>(redraws);
        _read = 0;
    }

    /// <inheritdoc/>
    public override long GetTimestamp()
    {
        if (_redraws.Contains(_read++))
        {
            _time += RedrawStep;
        }

        return _time;
    }

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => _inner.GetUtcNow();
}
