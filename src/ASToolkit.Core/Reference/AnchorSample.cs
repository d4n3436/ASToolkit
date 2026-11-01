namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents how one object stood by an anchor object in an original level.
/// </summary>
/// <param name="AnchorFacing">The facing of the anchor.</param>
/// <param name="Dx">The x offset of the object from the anchor.</param>
/// <param name="Dy">The y offset of the object from the anchor.</param>
/// <param name="Dz">The height of the object above the anchor.</param>
/// <param name="Facing">The facing of the object.</param>
public readonly record struct AnchorSample(byte AnchorFacing, float Dx, float Dy, float Dz, byte Facing);
