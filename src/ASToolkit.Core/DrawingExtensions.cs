using System.Drawing;
using System.Numerics;

namespace ASToolkit.Core;

/// <summary>
/// Provides coordinate conversions and deconstruction of <see cref="Point"/> and <see cref="Size"/> types.
/// </summary>
public static class DrawingExtensions
{
    extension(PointF point)
    {
        /// <summary>
        /// Deconstructs the point into its coordinates.
        /// </summary>
        public void Deconstruct(out float x, out float y)
        {
            x = point.X;
            y = point.Y;
        }
    }

    extension(Point point)
    {
        /// <summary>
        /// Converts the point to a vector.
        /// </summary>
        public Vector2 ToVector2() => new(point.X, point.Y);

        /// <summary>
        /// Deconstructs the point into its coordinates.
        /// </summary>
        public void Deconstruct(out int x, out int y)
        {
            x = point.X;
            y = point.Y;
        }
    }

    extension(SizeF size)
    {
        /// <summary>
        /// Deconstructs the size into its width and height.
        /// </summary>
        public void Deconstruct(out float width, out float height)
        {
            width = size.Width;
            height = size.Height;
        }
    }

    extension(Size size)
    {
        /// <summary>
        /// Deconstructs the size into its width and height.
        /// </summary>
        public void Deconstruct(out int width, out int height)
        {
            width = size.Width;
            height = size.Height;
        }
    }
}
