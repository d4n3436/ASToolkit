using ASToolkit.Core.Levels;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that the breaker hangs in view on the back wall of a room with its power equipment, and that only a
/// blackout breaker sets off an ambush (D1).
/// </summary>
public sealed class BreakerRoomRule : ILevelRule
{
    private const float RoomReach = 400;
    private const int OpenEndDistance = 3;
    private const int MinWallRun = 4;

    /// <inheritdoc/>
    public string Id => "D1";

    /// <summary>
    /// Returns the length in wall pieces of the back wall a breaker hangs on, or 0 when it hangs on none.
    /// </summary>
    public static int MeasureWallRun(LevelModel model, float x, float y)
    {
        var c = model.Lattice.GetCell(x, y);
        var wall = new EdgeKey('u', c.U + 1, c.V);
        return model.Edges.TryGetValue(wall, out char kind) && kind == 'W' ? WallRun.Find(model.Edges, wall).Length : 0;
    }

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        if (context.Target.Style == LevelStyle.Fenced)
        {
            yield break;
        }

        var a = context.Analysis;
        var model = context.Model;
        foreach (var breaker in a.Elements.Where(e => e.Kind == ElementKind.Breaker))
        {
            if (model is not null)
            {
                var c = model.Lattice.GetCell(breaker.X, breaker.Y);
                var wall = new EdgeKey('u', c.U + 1, c.V);
                bool onBackWall = model.Edges.TryGetValue(wall, out char kind) && kind == 'W';
                int front = FloorScanner.CountOpenCells(model.Cells.Contains, model.Edges.ContainsKey, c, -1, 0);
                int side = FloorScanner.CountOpenCells(model.Cells.Contains, model.Edges.ContainsKey, c, 0, 1);
                var run = WallRun.Find(model.Edges, wall);
                bool enclosed = onBackWall && run.IsEnclosed(OpenEndDistance);
                int minRun = Math.Min(MinWallRun, context.Target.BreakerWallRun);
                bool isLong = onBackWall && run.Length >= minRun;
                yield return new RuleResult("D1.1", RuleSeverity.Hard, onBackWall && front >= 2 && side >= 1 && enclosed && isLong,
                    $"back wall {onBackWall}, {run.Length} pieces long (at least {minRun}), ends in corners {enclosed}, open floor {front} cells in front and {side} beside");
            }

            int transformers = context.Map.Sprites.Count(s => s.Id == KnownObject.Transformer && RuleMath.GetDistance(s.Position.X, s.Position.Y, breaker.X, breaker.Y) <= RoomReach);
            yield return new RuleResult("D1.2", RuleSeverity.Soft, transformers >= 2, $"{transformers} transformers in the breaker room");
            int traps = a.Elements.Count(e => e is { Kind: ElementKind.Spawner, Program.Trigger: not null }
                                              && RuleMath.GetDistance(e.Program.Trigger.Value.X, e.Program.Trigger.Value.Y, breaker.X, breaker.Y) <= RoomReach);
            bool blackout = context.Target.Style == LevelStyle.Blackout;
            yield return new RuleResult("D1.3", RuleSeverity.Soft, blackout ? traps > 0 : traps == 0,
                $"{traps} ambushes trigger at the breaker, {(blackout ? "some" : "none")} expected");
        }
    }
}
