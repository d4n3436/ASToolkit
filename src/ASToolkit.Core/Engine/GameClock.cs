namespace ASToolkit.Core.Engine;

internal struct GameClock
{
    public const int DefaultFrameTime = 16;
    public const int MillisecondsPerSecond = 1000;
    private const int MaxFrameTime = 71;
    private const int FrameRateWindow = 999;

    public static readonly IReadOnlyList<int> DisplayFrameTimes = GetDisplayFrameTimes(60);

    private readonly IReadOnlyList<int> _frameTimes;
    private int _framesThisSecond;
    private int _secondStartedAt;

    public GameClock(IReadOnlyList<int> frameTimes)
    {
        _frameTimes = frameTimes;
        FrameTime = frameTimes[0];
        FramesPerSecond = (FrameRateWindow + FrameTime) / FrameTime;
    }

    public int Time { get; private set; }

    public int Tick { get; private set; }

    public int FrameTime { get; private set; }

    public int FramesPerSecond { get; private set; }

    public static int[] GetDisplayFrameTimes(int refreshRate)
    {
        int[] times = new int[refreshRate];
        for (int frame = 0; frame < refreshRate; frame++)
        {
            times[frame] = (frame + 1) * MillisecondsPerSecond / refreshRate - frame * MillisecondsPerSecond / refreshRate;
        }

        return times;
    }

    [EngineRoutine(0x0040f210)]
    public void Advance()
    {
        Tick++;
        FrameTime = Math.Min(_frameTimes[Tick % _frameTimes.Count], MaxFrameTime);
        Time += FrameTime;
        _framesThisSecond++;

        if (Time - _secondStartedAt > FrameRateWindow)
        {
            _secondStartedAt = Time;
            FramesPerSecond = _framesThisSecond;
            _framesThisSecond = 0;
        }
    }
}
