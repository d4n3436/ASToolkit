namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents the share of doors that open through a neck and the neck's fitted width, depth and owner.
/// </summary>
/// <param name="LinkShare">The share of doors with a neck on either side.</param>
/// <param name="EnteredShare">The share of necks on the room being entered.</param>
/// <param name="Width">The width of a neck in layout units.</param>
/// <param name="Length">The depth of a neck in layout units.</param>
public readonly record struct NeckFit(float LinkShare, float EnteredShare, LogNormalFit Width, LogNormalFit Length);
