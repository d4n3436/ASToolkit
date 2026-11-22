using System.Drawing;
using ASToolkit.Formats;
using static ASToolkit.Formats.KnownObject;

namespace ASToolkit.Core.Generation.Content;

// Offsets are map pixels for a slash wall's hole, mirrored on a back wall
internal sealed record SceneGrammar(
    PointF Site,
    PointF Spawner,
    float SpawnerZ,
    float TriggerRadius,
    int[] GeneratorBatch,
    float GeneratorJitter,
    float[] GeneratorRise,
    int[] Racks,
    int RackCells,
    int[] EndlessGeneratorRanks,
    float RubbleShare)
{
    private const int RankWindow = 9;

    public static SceneGrammar Default { get; } = new(new PointF(25, 20), new PointF(-15, -12), 10, 100, [2, 4], 2, [3, 9], [2, 0, 0, 0, 1], 80, [0, 0, 3, 6, 6, 8, 9, 10, 11, 14, 14], 0.25f);

    public KnownObject DrawEndlessGenerator(Random rng, int level, IReadOnlySet<KnownObject> excluded)
    {
        int top = EndlessGeneratorRanks[Math.Min(level, EndlessGeneratorRanks.Length - 1)];
        var ids = Enumerable.Range((int)FirstEndlessGenerator, top + 1)
            .Select(id => (KnownObject)id)
            .Where(id => id != RedEndlessGenerator1 && !excluded.Contains(id))
            .ToArray();

        var window = ids.Where(id => (int)id >= (int)FirstEndlessGenerator + top - RankWindow).ToArray();
        return window.Length > 0 ? window[rng.Next(window.Length)] : ids[rng.Next(ids.Length)];
    }
}
