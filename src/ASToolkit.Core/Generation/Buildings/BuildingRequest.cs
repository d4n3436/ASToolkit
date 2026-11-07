using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Generation.Buildings;

/// <summary>
/// Represents what a level asks of its building, such as its kind, gate, room count, style and closets.
/// </summary>
public sealed record BuildingRequest
{
    /// <summary>Gets what the building is.</summary>
    public required Building Building { get; init; }

    /// <summary>Gets the gate style of the level.</summary>
    public required LevelStyle Gate { get; init; }

    /// <summary>Gets the regular rooms the level asks for, halls and passages left out.</summary>
    public required int Rooms { get; init; }

    /// <summary>Gets the generation style of the level.</summary>
    public required GenerationStyle Style { get; init; }

    /// <summary>Gets a value indicating whether the level is the first of its style with its gate.</summary>
    public required bool IsIntro { get; init; }

    /// <summary>Gets a value indicating whether the level has dynamite sites.</summary>
    public required bool HasSites { get; init; }

    /// <summary>Gets the secret closets of the level.</summary>
    public required int SecretClosets { get; init; }

    /// <summary>Gets the spawn closets of the level.</summary>
    public required int SpawnClosets { get; init; }

    /// <summary>Gets the number of the level in its campaign.</summary>
    public int Level { get; init; }

    /// <summary>Gets the second wing of a lab, or null when the grammar draws it.</summary>
    public LabWing? SecondWing { get; init; }
}
