using System.Drawing;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Simulation;

/// <summary>
/// Represents a unit at one frame of a playthrough.
/// </summary>
/// <param name="Unit">The number of the unit within the playthrough.</param>
/// <param name="Id">The object of the unit.</param>
/// <param name="Position">The map position of the unit.</param>
/// <param name="Facing">The facing of the unit.</param>
/// <param name="Animation">The animation state of the unit.</param>
public readonly record struct UnitState(int Unit, KnownObject Id, PointF Position, byte Facing, SpriteAnimation Animation);
