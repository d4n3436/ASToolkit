using ASToolkit.Core.Formats;

namespace ASToolkit.Gui.Models;

/// <summary>
/// Represents an animation state of an object that has frames.
/// </summary>
/// <param name="State">The animation state.</param>
/// <param name="FrameCount">The number of frames in each direction.</param>
public sealed record AnimationState(SpriteAnimation State, int FrameCount)
{
    /// <summary>
    /// Gets the text shown for the state.
    /// </summary>
    public string Label => $"{(int)State} {State}  ({FrameCount} frames)";
}
