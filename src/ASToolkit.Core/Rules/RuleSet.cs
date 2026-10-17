using ASToolkit.Core.Rules.Checks;

namespace ASToolkit.Core.Rules;

/// <summary>
/// Represents the rules a level is checked against, in the order they report.
/// </summary>
public sealed class RuleSet
{
    private RuleSet(IReadOnlyList<ILevelRule> rules)
    {
        Rules = rules;
    }

    /// <summary>
    /// Gets the rule set of docs/RULES.md.
    /// </summary>
    public static RuleSet Default { get; } = new(
    [
        new ClutterRule(),
        new ReachabilityRule(),
        new TourLengthRule(),
        new RevisitRule(),
        new MonsterSplitRule(),
        new AmbushTimingRule(),
        new AmbushPlacementRule(),
        new CrateLootRule(),
        new CheckpointRule(),
        new PlacedGroupRule(),
        new DifficultyRule(),
        new ThemeDecorRule(),
        new WallClearanceRule(),
        new LayoutFillRule(),
        new SpineShareRule(),
        new WallFacingRule(),
        new LockedDoorGateRule(),
        new BlackoutGateRule(),
        new FenceGateRule(),
        new MildGateRule(),
        new DynamiteCountRule(),
        new DynamiteTimingRule(),
        new ChargeBatchRule(),
        new ChargeDistanceRule(),
        new FinalSiteRule(),
        new SiteShapeRule(),
        new SiteClearanceRule(),
        new BreakerRoomRule(),
        new GeneratorRule(),
        new BossRule(),
        new SecretClosetRule(),
        new MountedCannonRule(),
        new SpawnWallRule(),
        new WeaponRule()
    ]);

    /// <summary>
    /// Gets the rules.
    /// </summary>
    public IReadOnlyList<ILevelRule> Rules { get; }

    /// <summary>
    /// Evaluates a level against every rule.
    /// </summary>
    public RuleResult[] Evaluate(RuleContext context) => Rules.SelectMany(r => r.Evaluate(context)).ToArray();
}
