using System.Numerics;

namespace ASToolkit.Core.Scripting;

/// <summary>
/// Represents a script event of a level and the area that sets it off.
/// </summary>
/// <param name="Kind">The kind of the event.</param>
/// <param name="Position">The center of the area in the world, at the height of the sprite whose script runs the event.</param>
/// <param name="Radius">The radius the player has to come within, or 0 when the event does not wait for the player.</param>
/// <param name="Source">The position of the sprite whose script runs the event.</param>
/// <param name="Label">The description of the event.</param>
public sealed record ScriptMarker(ScriptMarkerKind Kind, Vector3 Position, float Radius, Vector3 Source, string Label)
{
    /// <summary>
    /// Gets the place the event sends the player or the units to, or null.
    /// </summary>
    public Vector3? Target { get; init; }
}
