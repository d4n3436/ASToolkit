using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Core.Levels;

/// <summary>
/// Represents the area around an object that the player can't enter.
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
    /// Returns the cells a solid object's collision box touches, sampled at its corners, edge midpoints and center.
    /// </summary>
    /// <param name="lattice">The lattice of the cells.</param>
    /// <param name="objects">The object table.</param>
    /// <param name="id">The object ID.</param>
    /// <param name="x">The x coordinate of the object's position.</param>
    /// <param name="y">The y coordinate of the object's position.</param>
    /// <returns>The cells the box touches, or an empty set if the object isn't solid or isn't in the table.</returns>
    public static HashSet<Cell> GetFootprint(Lattice lattice, ObjectTable objects, KnownObject id, float x, float y) =>
        objects.TryFind(id, out var obj) && obj.IsSolid ? Create(obj, x, y, ExtentShare, 0).GetSampledCells(lattice) : [];

    /// <summary>
    /// Creates the box of an object at a map point that reaches a share of the object's extent plus a margin.
    /// </summary>
    /// <param name="obj">The object.</param>
    /// <param name="x">The x coordinate of the box's center.</param>
    /// <param name="y">The y coordinate of the box's center.</param>
    /// <param name="share">The share of the object's extent that the box reaches.</param>
    /// <param name="grow">The margin added to the box on every side.</param>
    /// <returns>The box.</returns>
    public static CollisionBox Create(in GameObject obj, float x, float y, float share, float grow) => Create(obj.Size, x, y, share, grow);

    /// <summary>
    /// Creates the box of an object of the given size at a map point that reaches a share of the object's extent plus a margin.
    /// </summary>
    /// <param name="size">The size of the object.</param>
    /// <param name="x">The x coordinate of the box's center.</param>
    /// <param name="y">The y coordinate of the box's center.</param>
    /// <param name="share">The share of the object's extent that the box reaches.</param>
    /// <param name="grow">The margin added to the box on every side.</param>
    /// <returns>The box.</returns>
    public static CollisionBox Create(Vector3 size, float x, float y, float share, float grow) => new(x, y, share * size.X + grow, share * size.Y + grow);

    /// <summary>
    /// Determines whether a point lies in the box.
    /// </summary>
    /// <param name="x">The x coordinate of the point.</param>
    /// <param name="y">The y coordinate of the point.</param>
    /// <returns><see langword="true"/> if the point lies in the box; otherwise, <see langword="false"/>.</returns>
    public bool Contains(float x, float y) => MinX <= x && x <= MaxX && MinY <= y && y <= MaxY;

    /// <summary>
    /// Determines whether the box overlaps another box.
    /// </summary>
    /// <param name="other">The other box.</param>
    /// <returns><see langword="true"/> if the boxes overlap; otherwise, <see langword="false"/>.</returns>
    public bool Overlaps(CollisionBox other) => Math.Abs(X - other.X) < HalfWidth + other.HalfWidth && Math.Abs(Y - other.Y) < HalfHeight + other.HalfHeight;

    /// <summary>
    /// Returns the lattice cells under the box's corners, edge midpoints and center.
    /// </summary>
    /// <param name="lattice">The lattice of the cells.</param>
    /// <returns>The cells under the sampled points.</returns>
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
