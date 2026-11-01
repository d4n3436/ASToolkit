namespace ASToolkit.Core.Generation.Geometry;

/// <summary>
/// Specifies what a shape shows on screen before the roof is laid.
/// </summary>
internal enum PixelKind : byte
{
    /// <summary>Empty ground that no ground earth covers.</summary>
    Bare = 1,

    /// <summary>Floor outside the shown cells.</summary>
    Floor,

    /// <summary>Floor of a closet.</summary>
    ClosetFloor,

    /// <summary>A wall face seen from outside the shown cells.</summary>
    HiddenFace,

    /// <summary>Floor of a shown cell.</summary>
    ShownFloor,

    /// <summary>A wall face seen from a shown cell.</summary>
    ShownFace,

    /// <summary>A wall top above the roof.</summary>
    Cap
}
