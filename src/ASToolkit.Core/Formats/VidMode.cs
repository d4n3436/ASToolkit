namespace ASToolkit.Core.Formats;

/// <summary>
/// Specifies how the draw routines build the quads of a sprite's frames.
/// </summary>
public enum VidMode
{
    /// <summary>
    /// An upright quad, or a flat one, depending on the depth range of the sprite.
    /// </summary>
    UprightOrFlat = -1,

    /// <summary>
    /// An upright quad whose depth grows with the height up the frame, never turned by the facing.
    /// </summary>
    Upright = 0,

    /// <summary>
    /// A flat quad.
    /// </summary>
    Flat = 1,

    /// <summary>
    /// Two quads of eight vertices.
    /// </summary>
    DoubleQuad = 2,

    /// <summary>
    /// A triangle mesh built from the vertex data of each frame.
    /// </summary>
    Mesh = 3,

    /// <summary>
    /// A flat quad with no special handling.
    /// </summary>
    PlainFlat = 4,

    /// <summary>
    /// A flat quad used by omni-lights.
    /// </summary>
    Light = 5,

    /// <summary>
    /// A light glow whose bounds widen by up to 128 pixels.
    /// </summary>
    Glow = 6
}
