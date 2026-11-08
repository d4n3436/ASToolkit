using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Generation.Content;

/// <summary>
/// Represents the gameplay content a generated level receives.
/// </summary>
public sealed class LevelBudget
{
    /// <summary>Gets the monster hit points aimed for.</summary>
    public required int TargetHitPoints { get; init; }

    /// <summary>Gets the pickups, by ID, including crate loot.</summary>
    public required Dictionary<KnownObject, int> Items { get; init; }

    /// <summary>
    /// Gets the weapons still to place.
    /// </summary>
    public required List<KnownObject> Weapons { get; init; }

    /// <summary>Gets the number of dynamite sites.</summary>
    public required int DynamiteSites { get; init; }

    /// <summary>Gets the number of mounted cannons.</summary>
    public required int MountedCannons { get; init; }

    /// <summary>Gets the number of fence generators.</summary>
    public required int Generators { get; init; }

    /// <summary>Gets the endless generators the dynamite sites hold.</summary>
    public required int EndlessGenerators { get; init; }

}
