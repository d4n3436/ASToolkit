using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

/// <summary>
/// Represents the header of a demo record after the map file name, with the random seed and the game time the level starts at.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 8)]
public readonly struct DemoHeader
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DemoHeader"/> structure.
    /// </summary>
    /// <param name="seed">The seed of the random number generator.</param>
    /// <param name="time">The game time the level starts at, in milliseconds.</param>
    public DemoHeader(int seed, int time)
    {
        Seed = seed;
        Time = time;
    }

    /// <summary>
    /// The seed of the random number generator.
    /// </summary>
    [FieldOffset(0)]
    public readonly int Seed;

    /// <summary>
    /// The game time the level starts at, in milliseconds.
    /// </summary>
    [FieldOffset(4)]
    public readonly int Time;
}
