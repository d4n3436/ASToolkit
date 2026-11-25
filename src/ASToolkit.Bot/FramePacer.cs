using ASToolkit.Engine;

namespace ASToolkit.Bot;

/// <summary>
/// Provides the real times to step a map at, cycling through a list of frame lengths.
/// </summary>
public sealed class FramePacer
{
    /// <summary>
    /// Represents the milliseconds in a second.
    /// </summary>
    public const int MillisecondsPerSecond = 1000;

    /// <summary>
    /// Represents the frame length of a 60 Hz display in milliseconds.
    /// </summary>
    public const int DefaultFrameTime = 16;

    private readonly IReadOnlyList<int> _frameTimes;
    private int _frame;

    /// <summary>
    /// Initializes a new instance of the <see cref="FramePacer"/> class.
    /// </summary>
    /// <param name="frameTimes">The frame lengths in milliseconds, repeated in order.</param>
    public FramePacer(IReadOnlyList<int> frameTimes)
    {
        _frameTimes = frameTimes;
    }

    /// <summary>
    /// Gets the frame lengths of a 60 Hz display.
    /// </summary>
    public static IReadOnlyList<int> DisplayFrameTimes { get; } = GetDisplayFrameTimes(60);

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
    /// Returns the real time of the next frame after a map's current one.
    /// </summary>
    /// <param name="map">The map.</param>
    /// <returns>The real time in milliseconds.</returns>
    public uint GetNextRealTime(Map map) => map.RealTime + (uint)_frameTimes[(_frame + 1) % _frameTimes.Count];

    /// <summary>
    /// Returns the real time of the next frame after a map's current one and moves on to the next frame length.
    /// </summary>
    /// <param name="map">The map.</param>
    /// <returns>The real time in milliseconds.</returns>
    public uint TakeNextRealTime(Map map)
    {
        uint realTime = GetNextRealTime(map);
        _frame++;

        return realTime;
    }

    /// <summary>
    /// Steps a map one frame at the next real time.
    /// </summary>
    /// <param name="map">The map.</param>
    /// <returns><see langword="true"/> when the game is quitting; otherwise, <see langword="false"/>.</returns>
    public bool Step(Map map)
    {
        return map.TactAt(TakeNextRealTime(map));
    }

    /// <summary>
    /// Advances a map's clock to the next real time and runs its message pump.
    /// </summary>
    /// <param name="map">The map.</param>
    /// <returns><see langword="true"/> when the game is quitting; otherwise, <see langword="false"/>.</returns>
    public bool AdvanceClock(Map map)
    {
        map.SetRealTime(TakeNextRealTime(map));
        return map.AdvanceClock();
    }

    /// <summary>
    /// Restarts the cycle of frame lengths from the first one.
    /// </summary>
    public void Restart() => _frame = 0;
}
