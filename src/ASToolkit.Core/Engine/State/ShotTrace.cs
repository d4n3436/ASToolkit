using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents one shot of a playthrough from where it was fired to where it landed.
/// </summary>
/// <param name="Id">The object that carries the damage.</param>
/// <param name="From">The map position the shot left from.</param>
/// <param name="To">The map position the shot landed at.</param>
/// <param name="Fired">The time the shot was fired in milliseconds.</param>
/// <param name="Landed">The time the shot landed in milliseconds.</param>
public readonly record struct ShotTrace(KnownObject Id, Vector3 From, Vector3 To, int Fired, int Landed);
