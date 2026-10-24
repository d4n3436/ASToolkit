using System.Numerics;

namespace ASToolkit.Core.Scripting;

/// <summary>
/// Represents a trigger area that the player steps on to open sprites.
/// </summary>
/// <param name="Position">The center of the trigger area.</param>
/// <param name="Radius">The radius of the trigger area.</param>
/// <param name="Label">The description of the opening.</param>
/// <param name="Handles">The handles of the sprites the trigger opens.</param>
/// <param name="TimeLine">The <c>TimeLine</c> from which the trigger works.</param>
/// <param name="IsProximity">A value indicating whether the trigger works when the player comes near, with nothing to press.</param>
public sealed record OpeningTrigger(Vector3 Position, float Radius, string Label, IReadOnlyList<uint> Handles, int TimeLine = 0, bool IsProximity = false);
