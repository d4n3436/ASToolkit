using System.Text;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Routes;
using Xunit;

namespace ASToolkit.Tests.Routes;

public class RouteAnalyzerTests
{
    private static readonly Dictionary<ObjectiveKind, string> FixtureKinds = new()
    {
        [ObjectiveKind.DynamiteSite] = "site",
        [ObjectiveKind.Breaker] = "breaker",
        [ObjectiveKind.Generator] = "generator",
        [ObjectiveKind.Boss] = "boss",
        [ObjectiveKind.Trigger] = "trigger",
        [ObjectiveKind.Spawned] = "spawned",
        [ObjectiveKind.Monsters] = "monsters",
        [ObjectiveKind.Dynamite] = "dynamite",
        [ObjectiveKind.Blackout] = "blackout"
    };

    [Fact]
    public void Analyze_OriginalLevels_MatchTheFixtureTour()
    {
        string game = TestGameFolder.RequireFolder();
        var objects = ObjectTable.Load(Path.Combine(game, "objects.res"));
        var report = new StringBuilder();
        foreach (var expected in FixtureReader.Read<List<RouteFixture>>("routes.json"))
        {
            string level = expected.Name;
            var script = LevelScriptReader.Read(Path.Combine(game, "Maps", level[..^4] + ".lgc"));
            var a = RouteAnalyzer.Analyze(level, MapResourceBuilder.Load(Path.Combine(game, "Maps", level)), script, objects);
            Assert.NotNull(a);
            var problems = new List<string>();
            if (Math.Abs(a.ReachableCells - expected.Cells) > expected.Cells * 0.001f)
            {
                problems.Add($"cells {a.ReachableCells} vs {expected.Cells}");
            }

            if (a.RoutePixels != expected.RoutePx)
            {
                problems.Add($"route {a.RoutePixels} vs {expected.RoutePx}");
            }

            if (Math.Abs(a.RevisitShare - expected.RevisitShare) > 0.0005f)
            {
                problems.Add($"revisit {a.RevisitShare} vs {expected.RevisitShare}");
            }

            string kinds = string.Join(",", a.Objectives.Select(o => FixtureKinds[o.Kind]));
            string expectedKinds = string.Join(",", expected.Objectives.Select(o => o.Kind));
            if (kinds != expectedKinds)
            {
                problems.Add("objective order differs");
            }
            else if (a.Objectives.Zip(expected.Objectives).Any(t => Math.Abs(t.First.Progress - t.Second.P) > 0.0005f))
            {
                problems.Add("objective progress differs");
            }

            string legs = string.Join(",", a.Legs.Select(l => l.Pixels));
            string expectedLegs = string.Join(",", expected.Legs.Select(l => l.Px));
            if (legs != expectedLegs)
            {
                problems.Add($"legs {legs} vs {expectedLegs}");
            }

            CompareGate(problems, "locked_share", a.Gating.LockedShare, expected);
            CompareGate(problems, "fenced_off_share", a.Gating.FencedOffShare, expected);
            CompareGate(problems, "blackout_p", a.Gating.BlackoutProgress, expected);
            float? key = expected.Gates.TryGetValue("key_to_gate_px", out var k) && k.GetDouble() < 1e6f * 0.9f ? (float)k.GetDouble() : null;
            if (key != a.Gating.KeyToGatePixels)
            {
                problems.Add($"key_to_gate {a.Gating.KeyToGatePixels} vs {key}");
            }

            string checkpoints = string.Join(",", a.Checkpoints.Select(c => c.Progress));
            string expectedCheckpoints = string.Join(",", expected.Checkpoints.Select(c => c.P));
            if (checkpoints != expectedCheckpoints)
            {
                problems.Add($"checkpoints {checkpoints} vs {expectedCheckpoints}");
            }

            if (problems.Count > 0)
            {
                report.AppendLine($"{level}: {string.Join("; ", problems)}");
            }
        }

        Assert.True(report.Length == 0, report.ToString());
    }

    private static void CompareGate(List<string> problems, string name, float? actual, RouteFixture expected)
    {
        float? value = expected.Gates.TryGetValue(name, out var e) ? (float)e.GetDouble() : null;
        if (value is null != actual is null || (value is not null && Math.Abs(value.Value - actual!.Value) > 0.0005f))
        {
            problems.Add($"{name} {actual} vs {value}");
        }
    }
}
