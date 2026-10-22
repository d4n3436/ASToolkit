using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that every spawn wall that blows open faces along its wall (D6).
/// </summary>
public sealed class SpawnWallRule : ILevelRule
{
    /// <inheritdoc/>
    public string Id => "D6";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        foreach (var sprite in context.Map.Sprites.Where(s => s.Id is KnownObject.SpawnWallSlash or KnownObject.SpawnWallBack && s.Payload.Length > 8))
        {
            int facing = ((sprite.Facing + 64) & 255) * 2 >> 8;
            int expected = sprite.Id == KnownObject.SpawnWallSlash ? 0 : 1;
            yield return new RuleResult(Id, RuleSeverity.Hard, facing == expected, $"spawn wall {sprite.Id} at ({sprite.X:0}, {sprite.Y:0}) dir {sprite.Dir}");
        }
    }
}
