using ASToolkit.Core.Levels;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents the fitted parameters of a style's mission grammar.
/// </summary>
public sealed record MissionModel
{
    /// <summary>
    /// Gets the structure of the missions per theme.
    /// </summary>
    public required IReadOnlyDictionary<LevelTheme, MissionStructure> Structures { get; init; }

    /// <summary>
    /// Gets the share of critical path rooms holding a trigger per spawned share of the hit points.
    /// </summary>
    public required FittedValue<double> TriggerSlope { get; init; }

    /// <summary>
    /// Gets the chance that a blackout locks the doors behind the player.
    /// </summary>
    public required FittedValue<double> BlackoutSeal { get; init; }

    /// <summary>
    /// Gets the chance that a level with several generator rooms closes the way back with rubble.
    /// </summary>
    public required FittedValue<double> RubbleSeal { get; init; }

    /// <summary>
    /// Gets the chance that the last phase of a gate style is a return pass.
    /// </summary>
    public required IReadOnlyDictionary<LevelStyle, FittedValue<double>> LastReturn { get; init; }
}
