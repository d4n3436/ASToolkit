using System.Drawing;
using System.Numerics;

namespace ASToolkit.Engine;

public sealed partial class Graphics
{
    private const float ViewDepthOffset = 128f;

    /// <summary>
    /// Creates the view transform that moves the camera position to the top left of the viewport and raises points by their height.
    /// </summary>
    /// <param name="camera">The top left corner of the view, in map pixels.</param>
    /// <param name="viewport">The viewport of the screen, in pixels.</param>
    /// <returns>The view transform.</returns>
    public static Matrix4x4 CreateView(Vector2 camera, Rectangle viewport) => new(
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, -1f, 1f, 0f,
        -camera.X - (viewport.Right + (float)viewport.Left) * 0.5f, -camera.Y - (viewport.Bottom + (float)viewport.Top) * 0.5f, ViewDepthOffset, 1f);
}
