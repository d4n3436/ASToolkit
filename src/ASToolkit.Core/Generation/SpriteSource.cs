namespace ASToolkit.Core.Generation;

/// <summary>
/// Specifies where the generator got a sprite of a level from.
/// </summary>
public enum SpriteSource
{
    /// <summary>
    /// Walls, floor, roof, doors and fences laid from the layout.
    /// </summary>
    Layout,

    /// <summary>
    /// A piece of a wall window copied from an original.
    /// </summary>
    CopiedWindow,

    /// <summary>
    /// A piece of a furniture part copied whole from an original.
    /// </summary>
    CopiedPart,

    /// <summary>
    /// A piece of a scene or site copied from an original.
    /// </summary>
    CopiedScene,

    /// <summary>
    /// A monster or spawner of a group copied from an original.
    /// </summary>
    CopiedFormation,

    /// <summary>
    /// A piece drawn from the measured relations between pieces.
    /// </summary>
    Relation,

    /// <summary>
    /// A piece placed by the generator's own rules.
    /// </summary>
    Model
}
