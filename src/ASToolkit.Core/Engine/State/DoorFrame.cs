using System.Numerics;

using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents a door's position at a simulation time.
/// </summary>
/// <param name="Placement">The door placement.</param>
/// <param name="Time">The time in milliseconds.</param>
/// <param name="Position">The map position of the door.</param>
public sealed record DoorFrame(MergedMapSprite Placement, int Time, Vector3 Position);
