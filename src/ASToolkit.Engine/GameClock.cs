namespace ASToolkit.Engine;

/// <summary>
/// Represents the game clock.
/// </summary>
/// <remarks>
/// The clock keeps two times in milliseconds. Wall time is real time and keeps running regardless of what happens in the game.
/// Game time advances from it frame by frame, with each frame adding the wall time that passed, capped and scaled by the game speed.
/// </remarks>
public struct GameClock
{
    /// <summary>
    /// Represents the game time a level starts at.
    /// </summary>
    public const int LevelStartTime = 10;

    /// <summary>
    /// Represents the default wall time of a new clock.
    /// </summary>
    public const uint StartWallTime = 1;

    /// <summary>
    /// Represents the milliseconds in a second.
    /// </summary>
    public const int MillisecondsPerSecond = 1000;

    /// <summary>
    /// Represents the game time of the frame before the one a level starts at.
    /// </summary>
    public const int LevelStartPreviousTime = 1;
    private const int ResumeFrameTime = 10;
    private const uint MaxWallStep = 71;
    private const uint FrameRateWindow = 1000;
    private const float NormalSpeed = 1;

    private int _framesThisSecond;
    private uint _secondStartedAt;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameClock"/> structure at the start of a level.
    /// </summary>
    /// <param name="wallTime">The wall time of the start in milliseconds.</param>
    public GameClock(uint wallTime = StartWallTime)
    {
        Time = LevelStartTime;
        PreviousTime = LevelStartPreviousTime;
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
    /// Gets the game time in milliseconds of the previous frame.
    /// </summary>
    public int PreviousTime { get; private set; }

    /// <summary>
    /// Gets the length of the current frame in milliseconds.
    /// </summary>
    public int FrameTime { get; private set; }

    /// <summary>
    /// Gets the number of frames in the last full second of wall time, or 0 until the first second has passed.
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
    /// Sets the game time.
    /// </summary>
    /// <param name="time">The time in milliseconds.</param>
    public void SetTime(int time)
    {
        FrameTime = time - PreviousTime;
        Time = time;
    }

    /// <summary>
    /// Sets the game time back to the time a pause started, with a short last frame.
    /// </summary>
    /// <param name="time">The time in milliseconds.</param>
    public void Resume(int time)
    {
        Time = time;
        PreviousTime = time - ResumeFrameTime;
        FrameTime = ResumeFrameTime;
    }

    /// <summary>
    /// Advances the clock by one frame.
    /// </summary>
    /// <param name="firstRead">The wall time that ends the frame's step.</param>
    /// <param name="secondRead">The wall time of the new frame.</param>
    public void StartTact(uint firstRead, uint secondRead)
    {
        uint step = Math.Min(firstRead - WallTime, MaxWallStep);
        WallTime = secondRead;
        PreviousTime = Time;
        Time += (int)(step * Speed);
        FrameTime = Time - PreviousTime;
        _framesThisSecond++;

        if (WallTime - _secondStartedAt >= FrameRateWindow)
        {
            _secondStartedAt = WallTime;
            FramesPerSecond = _framesThisSecond;
            _framesThisSecond = 0;
        }
    }

    internal void ResetRate()
    {
        Speed = NormalSpeed;
        FramesPerSecond = 0;
        _framesThisSecond = 0;
    }

    internal void SetTimes(int previousTime, int time)
    {
        PreviousTime = previousTime;
        SetTime(time);
    }

    internal void SetWallTime(uint wallTime) => WallTime = wallTime;

    internal void Rewind()
    {
        Time = PreviousTime;
        FrameTime = 0;
    }
}
