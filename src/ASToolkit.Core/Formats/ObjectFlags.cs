namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents the flags of an entry of objects.res.
/// </summary>
[Flags]
public enum ObjectFlags : uint
{
    /// <summary>
    /// No flags.
    /// </summary>
    None = 0,

    /// <summary>
    /// The object blocks movement.
    /// </summary>
    Solid = 1 << 5,

    /// <summary>
    /// The object blocks movement until it is shot.
    /// </summary>
    Breakable = 1 << 6,

    /// <summary>
    /// The object is drawn over everything below it, as items are.
    /// </summary>
    Raised = 1 << 15,

    /// <summary>
    /// The object bobs up and down.
    /// </summary>
    Bobbing = 1 << 16,

    /// <summary>
    /// The visibility of the object depends on the level. Only object 631 has it.
    /// </summary>
    LevelDependent = 1 << 17,

    /// <summary>
    /// The animation of the object starts on frame 0 instead of a random frame.
    /// </summary>
    StartsOnFirstFrame = 1 << 24
}
