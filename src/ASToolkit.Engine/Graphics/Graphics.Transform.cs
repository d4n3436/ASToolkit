using System.Drawing;
using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public sealed partial class Graphics
{
    private const float ProjectionDepthScale = 0.001f;
    private const float ViewDepthOffset = 128f;
    private const float ProjectionRange = 2f;

    /// <summary>
    /// Creates the view transform that the camera sets on the device, moving the map so that the camera position lands at the top left of the clip area and raising points by their height.
    /// </summary>
    /// <param name="camera">The top left corner of the view, in map pixels.</param>
    /// <param name="clip">The clip area of the screen.</param>
    /// <returns>The view transform.</returns>
    public static Matrix4x4 CreateView(Vector2 camera, Rectangle clip) => new(
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, -1f, 1f, 0f,
        -camera.X - (clip.Right + (float)clip.Left) * 0.5f, -camera.Y - (clip.Bottom + (float)clip.Top) * 0.5f, ViewDepthOffset, 1f);

    /// <summary>
    /// Creates the projection transform that a clip area sets on the device, mapping its pixels one to one onto the viewport.
    /// </summary>
    /// <param name="clip">The clip area of the screen.</param>
    /// <returns>The projection transform.</returns>
    [EngineRoutine(0x00402400)]
    public static Matrix4x4 CreateProjection(Rectangle clip) => new(
        ProjectionRange / clip.Width, 0f, 0f, 0f,
        0f, -ProjectionRange / clip.Height, 0f, 0f,
        0f, 0f, ProjectionDepthScale, 0f,
        0f, 0f, 0f, 1f);
}
