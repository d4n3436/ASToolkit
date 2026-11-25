using System.Drawing;

namespace ASToolkit.Engine;

public sealed partial class Graphics
{
    /// <summary>
    /// Gets or sets the top-left corner of the window on the screen.
    /// </summary>
    public Point WindowPosition { get; set; }

    /// <summary>
    /// Gets or sets the top-left corner of the window's client area on the screen.
    /// </summary>
    public Point ClientPosition { get; set; }

    /// <summary>
    /// Gets or sets the device that frames are drawn on, or <see langword="null"/> to draw without one.
    /// </summary>
    public IGraphicsDevice? Device { get; set; }

    internal bool IsMenuLoopActive => Flags.HasFlag(GraphicsFlags.MenuLoop);

    [EngineRoutine(0x004335f0)]
    internal void BeginPause() => Flags |= GraphicsFlags.MenuLoop;

    [EngineRoutine(0x00433660)]
    internal void EndPause() => Flags &= ~GraphicsFlags.MenuLoop;
}
