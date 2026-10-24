using System.Numerics;

namespace ASToolkit.Core.Scripting;

/// <summary>
/// Represents a place the story of a level leads the player to.
/// </summary>
/// <param name="Position">The place in the world.</param>
/// <param name="Radius">The radius the player has to come within, or 0 when the player has to reach the place itself.</param>
/// <param name="Label">The description of the step.</param>
/// <param name="TimeLine">The latest <c>TimeLine</c> of the quests that have begun when the player heads to the step.</param>
/// <param name="IsTarget">A value indicating whether the step is a sprite the player has to destroy.</param>
public sealed record ProgressionStep(Vector3 Position, float Radius, string Label, int TimeLine, bool IsTarget = false);
