using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Simulation;

/// <summary>
/// Represents the player after the load or one frame of a replayed demo.
/// </summary>
/// <param name="Time">The game time of the frame in milliseconds.</param>
/// <param name="Position">The position of the player after the frame.</param>
/// <param name="Health">The health of the player after the frame.</param>
/// <param name="Direction">The direction of the player's legs after the frame.</param>
/// <param name="Speed">The speed of the player after the frame, in pixels per millisecond.</param>
/// <param name="RandomState">The state of the game's random generator after the frame.</param>
public readonly record struct DemoReplayPoint(int Time, Vector3 Position, int Health, Angle Direction, float Speed, uint RandomState);
