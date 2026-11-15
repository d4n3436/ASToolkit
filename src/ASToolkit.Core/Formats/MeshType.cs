namespace ASToolkit.Core.Formats;

/// <summary>
/// Specifies the mesh type of a sprite, the geometry the draw routines build for its frames.
/// </summary>
public enum MeshType
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
    /// A flat quad lying on the ground plane. The outline pass skips it.
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
    /// A flat quad drawn like <see cref="Flat"/>.
    /// </summary>
    PlainFlat = 4,

    /// <summary>
    /// A flat quad drawn like <see cref="Flat"/>, used by the omni-lights.
    /// </summary>
    Light = 5,

    /// <summary>
    /// A light glow drawn over everything already drawn, whose bounds widen by up to 128 pixels.
    /// </summary>
    Glow = 6
}
