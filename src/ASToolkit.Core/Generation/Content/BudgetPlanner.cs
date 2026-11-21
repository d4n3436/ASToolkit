using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Campaign;
using ASToolkit.Core.Reference;

namespace ASToolkit.Core.Generation.Content;

/// <summary>
/// Provides the budget of a generated level.
/// </summary>
public sealed class BudgetPlanner
{
    private readonly EncounterModel _encounters;

    /// <summary>
    /// Initializes a new instance of the <see cref="BudgetPlanner"/> class.
    /// </summary>
    /// <param name="encounters">The style's fitted fights.</param>
    public BudgetPlanner(EncounterModel encounters)
    {
        _encounters = encounters;
    }

    /// <summary>
    /// Plans the budget of a level, with hit points within a tenth of those in the brief.
    /// </summary>
    /// <param name="rng">The random source.</param>
    /// <param name="brief">The brief of the level.</param>
    /// <param name="dynamiteSites">The number of dynamite sites of the level.</param>
    /// <returns>The budget of the level.</returns>
    public LevelBudget Plan(Random rng, LevelBrief brief, int dynamiteSites)
    {
        var items = brief.Pickups.ToDictionary();
        if (dynamiteSites > 0)
        {
            items[KnownObject.Dynamite] = dynamiteSites;
        }

        return new LevelBudget
        {
            TargetHitPoints = (int)(brief.HitPoints * (0.9f + 0.2f * rng.NextSingle())),
            Items = items,
            Weapons = brief.Weapons,
            DynamiteSites = dynamiteSites,
            MountedCannons = brief.MountedCannons,
            EndlessGenerators = (int)MathF.Round(dynamiteSites * _encounters.GeneratorsPerSite.GetValueOrDefault(brief.WallSet).Value)
        };
    }
}
