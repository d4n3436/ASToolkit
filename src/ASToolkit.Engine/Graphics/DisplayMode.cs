using System.Drawing;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a display mode of the display adapter.
/// </summary>
/// <param name="Size">The screen size of the mode.</param>
/// <param name="Format">The pixel format of the mode.</param>
public readonly record struct DisplayMode(Size Size, VidPixelFormat Format);
