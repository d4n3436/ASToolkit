namespace ASToolkit.Gui.Models;

/// <summary>
/// Represents an animation state of an object that has frames.
/// </summary>
/// <param name="State">The index of the state.</param>
/// <param name="FrameCount">The number of frames in each direction.</param>
public sealed record AnimationState(int State, int FrameCount)
{
    /// <summary>
    /// Gets the text shown for the state.
    /// </summary>
    public string Label => $"{State}  ({FrameCount} frames)";
}
