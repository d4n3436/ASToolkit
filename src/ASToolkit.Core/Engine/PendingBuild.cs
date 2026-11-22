using System.Drawing;

namespace ASToolkit.Core.Engine;

internal readonly record struct PendingBuild(SpriteDefinition Definition, Point Offset);
