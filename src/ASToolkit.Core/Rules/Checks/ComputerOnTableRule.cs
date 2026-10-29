using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that every computer sits on a table (A18).
/// </summary>
public sealed class ComputerOnTableRule : ILevelRule
{
    private const int TableReach = 2;

    /// <inheritdoc/>
    public string Id => "A18";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        var model = context.Model;
        if (model is null)
        {
            yield break;
        }

        var tables = model.Objects.Where(o => o.Id == KnownObject.Table).ToArray();
        var computers = model.Objects.Where(o => o.Id is KnownObject.Computer or KnownObject.ComputerDead).ToArray();
        string[] offTable = computers
            .Where(computer => !tables.Any(table =>
                Math.Abs((int)MathF.Floor(computer.U) - (int)MathF.Floor(table.U)) +
                Math.Abs((int)MathF.Floor(computer.V) - (int)MathF.Floor(table.V)) <= TableReach))
            .Select(computer => $"{computer.Id} at ({computer.U:0},{computer.V:0})")
            .ToArray();

        yield return new RuleResult(Id, RuleSeverity.Soft, offTable.Length == 0,
            offTable.Length == 0 ? $"all {computers.Length} computers on a table" : $"{offTable.Length} of {computers.Length} computers off a table: {string.Join(", ", offTable.Take(5))}");
    }
}
