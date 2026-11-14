using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents a unit at one frame of a playthrough.
/// </summary>
/// <param name="Unit">The number of the unit within the playthrough.</param>
/// <param name="Id">The object of the unit.</param>
/// <param name="Position">The map position of the unit.</param>
/// <param name="Facing">The facing of the unit.</param>
/// <param name="Animation">The animation state of the unit.</param>
/// <param name="DiedAt">The time the unit died, or -1 while it is alive.</param>
/// <param name="Frame">The index of the frame the unit shows within its sprite.</param>
/// <param name="Weapon">The player's equipped torso, recorded as its own unit, or zero for the object's default attachment.</param>
/// <param name="Keyframe">The position of the unit in its keyframe animation, in keyframes.</param>
public readonly record struct UnitState(int Unit, KnownObject Id, Vector3 Position, byte Facing, SpriteAnimation Animation,
    int DiedAt, int Frame = 0, KnownObject Weapon = 0, float Keyframe = 0);
