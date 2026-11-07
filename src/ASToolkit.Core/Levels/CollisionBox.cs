using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Levels;

/// <summary>
/// Represents the area around an object that the player cannot enter.
/// </summary>
/// <param name="X">The x coordinate of the box's center.</param>
/// <param name="Y">The y coordinate of the box's center.</param>
/// <param name="HalfWidth">The half width of the box.</param>
/// <param name="HalfHeight">The half height of the box.</param>
public readonly record struct CollisionBox(float X, float Y, float HalfWidth, float HalfHeight)
{
    /// <summary>
    /// Represents the share of an object's extent that its collision box reaches.
    /// </summary>
    public const float ExtentShare = 0.33f;

    /// <summary>
    /// Represents the player's radius.
    /// </summary>
    public const float PlayerRadius = 14;

    /// <summary>
    /// Gets the left edge of the box.
    /// </summary>
    public float MinX => X - HalfWidth;

    /// <summary>
    /// Gets the right edge of the box.
    /// </summary>
    public float MaxX => X + HalfWidth;

    /// <summary>
    /// Gets the top edge of the box.
    /// </summary>
    public float MinY => Y - HalfHeight;

    /// <summary>
    /// Gets the bottom edge of the box.
    /// </summary>
    public float MaxY => Y + HalfHeight;

    /// <summary>
    /// Creates the box of an object at a map point, reaching a share of its extent plus a margin.
    /// </summary>
    public static CollisionBox Create(in GameObject obj, float x, float y, float share, float grow) => Create(obj.Size, x, y, share, grow);

    /// <summary>
    /// Creates the box of an object of the given size at a map point, reaching a share of its extent plus a margin.
    /// </summary>
    public static CollisionBox Create(Vector3 size, float x, float y, float share, float grow) => new(x, y, share * size.X + grow, share * size.Y + grow);

    /// <summary>
    /// Returns whether a point lies in the box.
    /// </summary>
    public bool Contains(float x, float y) => MinX <= x && x <= MaxX && MinY <= y && y <= MaxY;

    /// <summary>
    /// Returns whether the box overlaps another box.
    /// </summary>
    public bool Overlaps(CollisionBox other) => Math.Abs(X - other.X) < HalfWidth + other.HalfWidth && Math.Abs(Y - other.Y) < HalfHeight + other.HalfHeight;

    /// <summary>
    /// Returns the lattice cells under the box's corners, edge midpoints and center.
    /// </summary>
    public HashSet<Cell> GetSampledCells(Lattice lattice)
    {
        var cells = new HashSet<Cell>();
        ReadOnlySpan<float> across = [-HalfWidth, 0, HalfWidth];
        ReadOnlySpan<float> down = [-HalfHeight, 0, HalfHeight];
        foreach (float dx in across)
        {
            foreach (float dy in down)
            {
                cells.Add(lattice.GetCell(X + dx, Y + dy));
            }
        }

        return cells;
    }
}
