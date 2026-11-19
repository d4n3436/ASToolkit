using System.Numerics;

namespace ASToolkit.Core.Simulation;

/// <summary>
/// Represents the center of the screen and the mouse cursor at one tick of a playthrough.
/// </summary>
/// <param name="Time">The time of the tick, in milliseconds.</param>
/// <param name="Center">The map point at the center of the screen.</param>
/// <param name="Cursor">The ground point under the mouse cursor.</param>
public readonly record struct ScreenInput(int Time, Vector2 Center, Vector2 Cursor) : ITimed;
