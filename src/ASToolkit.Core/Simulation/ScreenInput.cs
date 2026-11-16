using System.Numerics;

namespace ASToolkit.Core.Simulation;

/// <summary>
/// Represents the player's position and the mouse cursor the camera follows at one tick of a playthrough.
/// </summary>
/// <param name="Time">The time of the tick, in milliseconds.</param>
/// <param name="Player">The map position and height of the player.</param>
/// <param name="Cursor">The ground point under the mouse cursor.</param>
public readonly record struct ScreenInput(int Time, Vector3 Player, Vector2 Cursor) : ITimed;
