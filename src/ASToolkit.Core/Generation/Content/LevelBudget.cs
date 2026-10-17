using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Generation.Content;

/// <summary>
/// Represents the gameplay content a generated level receives.
/// </summary>
public sealed class LevelBudget
{
    /// <summary>Gets the monster hit points aimed for.</summary>
    public required int TargetHitPoints { get; init; }

    /// <summary>Gets the placed monster groups.</summary>
    public required List<IReadOnlyList<GroupMember>> PlacedGroups { get; init; }

    /// <summary>Gets the monster lists of the ambushes.</summary>
    public required List<IReadOnlyList<KnownObject>> Ambushes { get; init; }

    /// <summary>Gets the placed bosses, by ID.</summary>
    public required IReadOnlyDictionary<KnownObject, int> Bosses { get; init; }

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

    /// <summary>Gets the hit points of the placed groups.</summary>
    public required int PlacedHitPoints { get; init; }

    /// <summary>Gets the hit points of the ambushes.</summary>
    public required int AmbushHitPoints { get; init; }
}
