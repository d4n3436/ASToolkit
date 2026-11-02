using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Survey;

/// <summary>
/// Represents a unit a level's script builds once the player enters a trigger area.
/// </summary>
/// <param name="Trigger">The place of the trigger area.</param>
/// <param name="Id">The object the script builds.</param>
/// <param name="Place">The place the unit is built in.</param>
public readonly record struct LevelBuild(int Trigger, KnownObject Id, int Place);
