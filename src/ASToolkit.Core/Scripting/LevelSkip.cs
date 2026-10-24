using System.Numerics;

namespace ASToolkit.Core.Scripting;

/// <summary>
/// Represents a key press that moves the player to another place once a quest has begun.
/// </summary>
/// <param name="TimeLine">The <c>TimeLine</c> at which the quest begins.</param>
/// <param name="Destination">The place the player moves to.</param>
/// <param name="Label">The description of the skip.</param>
public sealed record LevelSkip(int TimeLine, Vector3 Destination, string Label);
