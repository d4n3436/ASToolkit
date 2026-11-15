using ASToolkit.Core.Formats;
namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents how one object stood by an anchor object in an original level.
/// </summary>
/// <param name="AnchorDirection">The direction of the anchor.</param>
/// <param name="Dx">The x offset of the object from the anchor.</param>
/// <param name="Dy">The y offset of the object from the anchor.</param>
/// <param name="Dz">The height of the object above the anchor.</param>
/// <param name="Direction">The direction of the object.</param>
public readonly record struct AnchorSample(Angle AnchorDirection, float Dx, float Dy, float Dz, Angle Direction);
