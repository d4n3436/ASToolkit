using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

/// <summary>
/// Represents one frame of a demo, with the game time the frame starts at and its input.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 36)]
public readonly struct DemoFrame
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DemoFrame"/> structure.
    /// </summary>
    /// <param name="time">The game time of the frame in milliseconds.</param>
    /// <param name="input">The input of the frame.</param>
    public DemoFrame(int time, in DemoInput input)
    {
        Time = time;
        Input = input;
    }

    /// <summary>
    /// The game time of the frame in milliseconds.
    /// </summary>
    [FieldOffset(0)]
    public readonly int Time;

    /// <summary>
    /// The input of the frame.
    /// </summary>
    [FieldOffset(4)]
    public readonly DemoInput Input;
}
