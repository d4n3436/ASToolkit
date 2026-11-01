using System.Drawing;

namespace ASToolkit.Core.Levels;

/// <summary>
/// Represents a straight segment between two map points.
/// </summary>
/// <param name="From">The start of the segment.</param>
/// <param name="To">The end of the segment.</param>
public readonly record struct MapSegment(PointF From, PointF To)
{
    /// <summary>
    /// Returns the distance from a map point to the nearest point of the segment.
    /// </summary>
    public float MeasureDistance(PointF point)
    {
        float dx = To.X - From.X;
        float dy = To.Y - From.Y;
        float length = dx * dx + dy * dy;
        float t = length <= 0 ? 0 : Math.Clamp(((point.X - From.X) * dx + (point.Y - From.Y) * dy) / length, 0, 1);
        float nearX = From.X + t * dx - point.X;
        float nearY = From.Y + t * dy - point.Y;
        return MathF.Sqrt(nearX * nearX + nearY * nearY);
    }
}
