namespace ASToolkit.Core.Formats;

/// <summary>
/// Specifies the layer of an object, which sets the list the engine updates it in and the order it is drawn in.
/// </summary>
public enum ObjectLayer
{
    /// <summary>
    /// Hardware sprites with depth values that do not blend, and the prerendered ground.
    /// </summary>
    Ground,

    /// <summary>
    /// Scenery baked into the terrain that does not blend.
    /// </summary>
    Built,

    /// <summary>
    /// Scenery baked into the terrain that blends with the pixels under it.
    /// </summary>
    BuiltAlpha,

    /// <summary>
    /// Software sprites of ground decals.
    /// </summary>
    SoftwareGround,

    /// <summary>
    /// Hardware sprites of ground decals.
    /// </summary>
    Ground2,

    /// <summary>
    /// Software sprites of objects built into the height grid of the map.
    /// </summary>
    SoftwareGrid,

    /// <summary>
    /// Other software sprites.
    /// </summary>
    Software,

    /// <summary>
    /// Software sprites that blend with the pixels under them.
    /// </summary>
    SoftwareAlpha,

    /// <summary>
    /// Other hardware sprites.
    /// </summary>
    Hardware,

    /// <summary>
    /// Hardware sprites that blend with the pixels under them and bob up and down or lie on the ground.
    /// </summary>
    AlphaGround,

    /// <summary>
    /// Hardware sprites of regions.
    /// </summary>
    Region,

    /// <summary>
    /// Omni-lights.
    /// </summary>
    Light,

    /// <summary>
    /// Other hardware sprites that blend with the pixels under them.
    /// </summary>
    Alpha,

    /// <summary>
    /// Software sprites drawn over everything below them.
    /// </summary>
    SoftwareTop,

    /// <summary>
    /// The cursor and fonts, and the light glows of Alien Shooter 2.
    /// </summary>
    Cursor,

    /// <summary>
    /// Menu frames and text, and objects without pictures.
    /// </summary>
    Menu
}
