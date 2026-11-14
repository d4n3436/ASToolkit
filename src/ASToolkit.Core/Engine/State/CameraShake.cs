using System.Numerics;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents the camera offset an earthquake gives one frame of a playthrough.
/// </summary>
/// <param name="Time">The time of the frame in milliseconds.</param>
/// <param name="Offset">The offset of the camera center in pixels.</param>
public readonly record struct CameraShake(int Time, Vector2 Offset) : ITimed;
