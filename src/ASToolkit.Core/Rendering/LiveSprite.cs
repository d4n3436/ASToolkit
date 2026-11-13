using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Rendering;

/// <summary>
/// Represents a sprite drawn at a chosen frame outside a map's own placements, such as a simulated unit or shot.
/// </summary>
/// <param name="Id">The object ID in objects.res.</param>
/// <param name="Position">The position in map pixels, with the height above the floor.</param>
/// <param name="Facing">The facing the object is drawn with.</param>
/// <param name="FrameIndex">The sprite frame of the live sprite to draw.</param>
public readonly record struct LiveSprite(KnownObject Id, Vector3 Position, byte Facing, int FrameIndex);
