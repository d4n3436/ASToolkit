using System.Drawing;
using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Content;

/// <summary>
/// Represents the mounted cannon and dynamite sites placed before a level's decor.
/// </summary>
/// <param name="Cannon">The cannon's hall and the far point of its kill zone, or null when the level has no cannon.</param>
/// <param name="Sites">The site positions and cells.</param>
public sealed record PlacedSites((PlanRegion Hall, PointF Far)? Cannon, (float X, float Y, Cell Cell)[] Sites);
