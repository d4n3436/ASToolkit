using System.Drawing;

namespace ASToolkit.Engine;

public sealed partial class Graphics
{
    /// <summary>
    /// Gets the top-left corner of the window on the screen.
    /// </summary>
    public Point WindowPosition => _window?.WindowRect.Location ?? Point.Empty;

    /// <summary>
    /// Gets the top-left corner of the window's client area on the screen.
    /// </summary>
    public Point ClientPosition => _window?.ClientRect.Location ?? Point.Empty;

    internal bool IsMenuLoopActive => Flags.HasFlag(GraphicsFlags.MenuLoop);

    [EngineRoutine(0x004335f0)]
    internal void BeginPause()
    {
        Flags |= GraphicsFlags.MenuLoop;
        FlipToGdi();
        Movie.Pause();
    }

    [EngineRoutine(0x00433660)]
    internal void EndPause()
    {
        Flags &= ~GraphicsFlags.MenuLoop;
        Movie.Resume();
    }
}
