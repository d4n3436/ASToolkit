namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents the game clock advancing by each frame's clamped real-time step.
/// </summary>
public struct GameClock
{
    /// <summary>
    /// Represents the frame length of a 60 Hz display in milliseconds.
    /// </summary>
    public const int DefaultFrameTime = 16;

    /// <summary>
    /// Represents the clock a level starts at.
    /// </summary>
    public const int LevelStartTime = 10;

    /// <summary>
    /// Represents the milliseconds in a second.
    /// </summary>
    public const int MillisecondsPerSecond = 1000;

    private const int LevelStartPreviousTime = 1;
    private const int ResumeFrameTime = 10;
    private const uint MaxWallStep = 71;
    private const uint FrameRateWindow = 1000;
    private const float NormalSpeed = 1;

    /// <summary>
    /// Represents the frame lengths of a 60 Hz display.
    /// </summary>
    public static readonly IReadOnlyList<int> DisplayFrameTimes = GetDisplayFrameTimes(60);

    private readonly IReadOnlyList<int> _frameTimes;
    private int _framesThisSecond;
    private uint _secondStartedAt;

    /// <summary>
    /// Creates a clock at the level start that cycles through frame lengths of wall time.
    /// </summary>
    /// <param name="frameTimes">The frame lengths in milliseconds the harness feeds as wall time.</param>
    /// <param name="wallTime">The wall time of the level start in milliseconds.</param>
    public GameClock(IReadOnlyList<int> frameTimes, uint wallTime = 0)
    {
        _frameTimes = frameTimes;
        Time = LevelStartTime;
        FrameTime = LevelStartTime - LevelStartPreviousTime;
        WallTime = wallTime;
        _secondStartedAt = wallTime;
        Speed = NormalSpeed;
    }

    /// <summary>
    /// Gets the game time in milliseconds.
    /// </summary>
    public int Time { get; private set; }

    /// <summary>
    /// Gets the number of frames run.
    /// </summary>
    public int Tick { get; private set; }

    /// <summary>
    /// Gets the length of the current frame in milliseconds.
    /// </summary>
    public int FrameTime { get; private set; }

    /// <summary>
    /// Gets the frames counted in the last full window of wall time, 0 until the first window closes.
    /// </summary>
    public int FramesPerSecond { get; private set; }

    /// <summary>
    /// Gets the wall time of the current frame in milliseconds.
    /// </summary>
    public uint WallTime { get; private set; }

    /// <summary>
    /// Gets the factor the game clock advances by per millisecond of wall time.
    /// </summary>
    public float Speed { get; private set; }

    /// <summary>
    /// Returns the frame lengths of a display's refresh rate over one second.
    /// </summary>
    /// <param name="refreshRate">The refresh rate in hertz.</param>
    /// <returns>The frame lengths in milliseconds.</returns>
    public static int[] GetDisplayFrameTimes(int refreshRate)
    {
        int[] times = new int[refreshRate];
        for (int frame = 0; frame < refreshRate; frame++)
        {
            times[frame] = (frame + 1) * MillisecondsPerSecond / refreshRate - frame * MillisecondsPerSecond / refreshRate;
        }

        return times;
    }

    /// <summary>
    /// Returns the wall time of the next frame from the frame lengths.
    /// </summary>
    /// <returns>The wall time in milliseconds.</returns>
    public readonly uint GetNextWallTime() => WallTime + (uint)_frameTimes[(Tick + 1) % _frameTimes.Count];

    /// <summary>
    /// Sets the game time.
    /// </summary>
    /// <param name="time">The time in milliseconds.</param>
    public void SetTime(int time)
    {
        FrameTime = time - (Time - FrameTime);
        Time = time;
    }

    /// <summary>
    /// Sets the game time back to the time a pause started, with a short last frame.
    /// </summary>
    /// <param name="time">The time in milliseconds.</param>
    public void Resume(int time)
    {
        Time = time;
        FrameTime = ResumeFrameTime;
    }

    /// <summary>
    /// Advances the clock to a frame read from the wall clock.
    /// </summary>
    /// <param name="firstRead">The first wall time read that differs from the current one, setting the step.</param>
    /// <param name="secondRead">The second wall time read, kept as the current one.</param>
    public void StartTact(uint firstRead, uint secondRead)
    {
        uint step = Math.Min(firstRead - WallTime, MaxWallStep);
        WallTime = secondRead;
        int previous = Time;
        Time += (int)(step * (double)Speed);
        FrameTime = Time - previous;
        Tick++;
        _framesThisSecond++;

        if (WallTime - _secondStartedAt >= FrameRateWindow)
        {
            _secondStartedAt = WallTime;
            FramesPerSecond = _framesThisSecond;
            _framesThisSecond = 0;
        }
    }
}
