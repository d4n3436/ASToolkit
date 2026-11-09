using System.Drawing;
using ASToolkit.Core.Formats;
using static ASToolkit.Core.Formats.KnownObject;

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
    int[] EndlessGeneratorRanks)
{
    private const int RankWindow = 9;

    public static SceneGrammar Default { get; } = new(new PointF(25, 20), new PointF(-15, -12), 10, 100, [2, 4], 2, [3, 9], [2, 0, 0, 0, 1], 80, [0, 0, 3, 6, 6, 8, 9, 10, 11, 14, 14]);

    public KnownObject DrawEndlessGenerator(Random rng, int level)
    {
        int top = EndlessGeneratorRanks[Math.Min(level, EndlessGeneratorRanks.Length - 1)];
        var ids = Enumerable.Range((int)FirstEndlessGenerator + Math.Max(0, top - RankWindow), Math.Min(top, RankWindow) + 1)
            .Select(id => (KnownObject)id)
            .Where(Enum.IsDefined)
            .ToArray();

        return ids[rng.Next(ids.Length)];
    }
}
