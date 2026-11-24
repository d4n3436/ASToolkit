using System.Drawing;

namespace ASToolkit.Engine;

/// <summary>
/// Defines the Direct3D device that frames are drawn on.
/// </summary>
public interface IGraphicsDevice
{
    /// <summary>
    /// Begins a scene.
    /// </summary>
    /// <returns>0 on success; otherwise, a negative error code.</returns>
    int BeginScene();

    /// <summary>
    /// Ends the scene.
    /// </summary>
    /// <returns>0 on success; otherwise, a negative error code.</returns>
    int EndScene();

    /// <summary>
    /// Presents the back buffer.
    /// </summary>
    /// <param name="source">The area of the back buffer to present, in back buffer pixels.</param>
    /// <param name="destination">The area of the window's client area to present it to.</param>
    /// <returns>0 on success; otherwise, a negative error code.</returns>
    int Present(Rectangle source, Rectangle destination);
}
