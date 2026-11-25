using System.Drawing;
using System.Numerics;

namespace ASToolkit.Engine;

public sealed partial class Graphics
{
    private const float ViewDepthOffset = 128f;

    /// <summary>
    /// Creates the view transform that moves the shift to the top left of the viewport and raises points by their height.
    /// </summary>
    /// <param name="shift">The top left corner of the view, in map pixels.</param>
    /// <param name="viewport">The viewport of the screen, in pixels.</param>
    /// <returns>The view transform.</returns>
    public static Matrix4x4 CreateView(Vector2 shift, Rectangle viewport) => new(
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, -1f, 1f, 0f,
        -shift.X - (viewport.Right + (float)viewport.Left) * 0.5f, -shift.Y - (viewport.Bottom + (float)viewport.Top) * 0.5f, ViewDepthOffset, 1f);
}
