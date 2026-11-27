using System.Drawing;

namespace ASToolkit.Engine;

/// <summary>
/// Defines the Direct3D device that frames are drawn on.
/// </summary>
public interface IGraphicsDevice
{
    /// <summary>
    /// Creates the device that presents a back buffer to the game window.
    /// </summary>
    /// <param name="backBuffer">The back buffer frames are drawn on.</param>
    /// <param name="isWindowed"><see langword="true"/> to present to the window; <see langword="false"/> to take over the display at the back buffer's size.</param>
    /// <param name="isVSync"><see langword="true"/> to wait for the display's vertical sync before each present; otherwise, <see langword="false"/>.</param>
    void CreateDevice(Screen backBuffer, bool isWindowed, bool isVSync)
    {
    }

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
