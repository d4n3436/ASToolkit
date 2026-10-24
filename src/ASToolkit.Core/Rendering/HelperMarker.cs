using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Rendering;

/// <summary>
/// Represents a helper (hidden) sprite.
/// </summary>
/// <param name="Sprite">The sprite.</param>
/// <param name="Kind">The role of the sprite.</param>
public sealed record HelperMarker(MergedMapSprite Sprite, HelperKind Kind);
