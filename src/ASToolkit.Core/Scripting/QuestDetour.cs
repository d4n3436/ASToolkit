namespace ASToolkit.Core.Scripting;

/// <summary>
/// Represents the steps of a quest that opens sprites.
/// </summary>
/// <param name="TimeLine">The <c>TimeLine</c> at which the quest begins.</param>
/// <param name="Steps">The places the quest leads the player to.</param>
public sealed record QuestDetour(int TimeLine, IReadOnlyList<ProgressionStep> Steps);
