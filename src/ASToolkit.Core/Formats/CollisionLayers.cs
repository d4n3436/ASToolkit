namespace ASToolkit.Core.Formats;

/// <summary>
/// Specifies the independent layers whose shared bits allow objects to collide.
/// </summary>
[Flags]
public enum CollisionLayers : uint
{
    /// <summary>
    /// No collision layers.
    /// </summary>
    None = 0,

    /// <summary>
    /// The first collision layer.
    /// </summary>
    Layer1 = 1 << 0,

    /// <summary>
    /// The second collision layer.
    /// </summary>
    Layer2 = 1 << 1,

    /// <summary>
    /// The third collision layer.
    /// </summary>
    Layer3 = 1 << 2,

    /// <summary>
    /// The fourth collision layer.
    /// </summary>
    Layer4 = 1 << 3,

    /// <summary>
    /// The fifth collision layer.
    /// </summary>
    Layer5 = 1 << 4
}
