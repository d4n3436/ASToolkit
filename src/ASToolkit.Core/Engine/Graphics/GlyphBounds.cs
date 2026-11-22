using System.Numerics;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents the area of a glyph in the texture of a font.
/// </summary>
/// <param name="Min">The texture coordinates of the top left corner.</param>
/// <param name="Max">The texture coordinates of the bottom right corner.</param>
public readonly record struct GlyphBounds(Vector2 Min, Vector2 Max);
