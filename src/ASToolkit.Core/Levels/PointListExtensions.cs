using System.Drawing;

namespace ASToolkit.Core.Levels;

/// <summary>
/// Provides groupings of map points.
/// </summary>
public static class PointListExtensions
{
    extension(IReadOnlyList<PointF> points)
    {
        /// <summary>
        /// Returns the groups of points that a chain of steps within a reach joins.
        /// </summary>
        /// <param name="reach">The maximum distance between two points of a chain.</param>
        /// <returns>The groups of points.</returns>
        public IReadOnlyList<IReadOnlyList<PointF>> Cluster(float reach)
        {
            var groups = new List<IReadOnlyList<PointF>>();
            var left = new List<PointF>(points);
            while (left.Count > 0)
            {
                var group = new List<PointF> { left[^1] };
                left.RemoveAt(left.Count - 1);
                for (int i = 0; i < group.Count; i++)
                {
                    var point = group[i];
                    for (int other = left.Count - 1; other >= 0; other--)
                    {
                        float dx = left[other].X - point.X;
                        float dy = left[other].Y - point.Y;
                        if (MathF.Sqrt(dx * dx + dy * dy) <= reach)
                        {
                            group.Add(left[other]);
                            left.RemoveAt(other);
                        }
                    }
                }

                groups.Add(group);
            }

            return groups;
        }
    }
}
