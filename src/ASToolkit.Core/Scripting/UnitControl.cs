using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Scripting;

/// <summary>
/// Represents a place where the player takes control of a unit.
/// </summary>
/// <param name="Position">The center of the trigger area.</param>
/// <param name="Radius">The radius of the trigger area.</param>
/// <param name="Unit">The object of the unit.</param>
public sealed record UnitControl(Vector3 Position, float Radius, KnownObject Unit);
