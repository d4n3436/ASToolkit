using System.Numerics;

using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Simulation;

/// <summary>
/// Represents a moved placement's position at a simulation time.
/// </summary>
/// <param name="Placement">The placement of the door or pushed object.</param>
/// <param name="Time">The time in milliseconds.</param>
/// <param name="Position">The map position of the placement.</param>
public sealed record PlacementFrame(MergedMapSprite Placement, int Time, Vector3 Position) : ITimed;
