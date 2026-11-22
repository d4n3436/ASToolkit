using System.Drawing;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents one rectangle of a texture that a hardware sprite draws as a quad.
/// </summary>
public struct HardwareTile
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HardwareTile"/> structure.
    /// </summary>
    /// <param name="texture">The index of the tile's texture.</param>
    /// <param name="size">The size of the tile, in pixels.</param>
    /// <param name="origin">The position of the tile on the map.</param>
    /// <param name="target">The colors and depths of the texture.</param>
    /// <param name="offset">The offset of the tile in the texture.</param>
    public HardwareTile(int texture, Size size, Point origin, Screen target, Point offset = default)
    {
        Offset = offset;
        Texture = texture;
        Size = size;
        Origin = origin;
        Target = target;
    }

    /// <summary>
    /// Gets the index of the tile's texture.
    /// </summary>
    public int Texture { get; }

    /// <summary>
    /// Gets the offset of the tile in its texture, in pixels.
    /// </summary>
    public Point Offset { get; }

    /// <summary>
    /// Gets the size of the tile, in pixels.
    /// </summary>
    public Size Size { get; }

    /// <summary>
    /// Gets the position of the tile on the map.
    /// </summary>
    public Point Origin { get; }

    /// <summary>
    /// Gets or sets the index of the next tile, or 0 for the last tile.
    /// </summary>
    public int Next { get; set; }

    /// <summary>
    /// Gets the colors and depths of the texture.
    /// </summary>
    public Screen Target { get; }
}
