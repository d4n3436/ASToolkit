using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Levels;

/// <summary>
/// Represents how close the original levels place each decor object to a wall, in cells.
/// </summary>
public sealed class WallClearanceTable
{
    private const int MinSamples = 20;
    private const int MinDropSamples = 8;
    private const float LowQuantile = 0.05f;
    private const float FloorHeight = 5;
    private const float DropReach = 160;

    private readonly Dictionary<KnownObject, (float Minimum, float Low)> _byId;
    private readonly Dictionary<KnownObject, (float Drop, float Side)> _dropById;

    private WallClearanceTable(Dictionary<KnownObject, (float, float)> byId, Dictionary<KnownObject, (float, float)> dropById)
    {
        _byId = byId;
        _dropById = dropById;
    }

    /// <summary>
    /// Measures every decor object standing on the floor of the given levels, and every floor piece and pickup against the wall below it on screen.
    /// </summary>
    public static WallClearanceTable Build(IReadOnlyList<LevelModel> models, ObjectTable objects)
    {
        var samples = new Dictionary<KnownObject, List<float>>();
        foreach (var model in models)
        {
            foreach (var levelObject in model.Objects.Where(o => o.Z < FloorHeight))
            {
                float d = model.Edges.MeasureClearance(levelObject.U, levelObject.V);
                if (float.IsInfinity(d))
                {
                    continue;
                }

                if (!samples.TryGetValue(levelObject.Id, out var list))
                {
                    list = [];
                    samples[levelObject.Id] = list;
                }

                list.Add(d);
            }
        }

        var byId = new Dictionary<KnownObject, (float, float)>();
        foreach (var (id, list) in samples)
        {
            list.Sort();
            float low = list.Count >= MinSamples ? list[(int)(LowQuantile * list.Count)] : list[0];
            byId[id] = (list[0], low);
        }

        return new WallClearanceTable(byId, BuildDrops(models, objects));
    }

    // Back a piece with too few samples off to its family, and keep every pickup to the low quantile of all pickups
    private static Dictionary<KnownObject, (float, float)> BuildDrops(IReadOnlyList<LevelModel> models, ObjectTable objects)
    {
        var drops = new Dictionary<KnownObject, List<float>>();
        foreach (var model in models)
        {
            foreach (var sprite in model.Sprites.Where(sprite => sprite.Id.Family is not null && sprite.Position.Z < FloorHeight || sprite.Id.PickupCategory is not null))
            {
                (drops.TryGetValue(sprite.Id, out var list) ? list : drops[sprite.Id] = []).Add(MeasureDrop(model.Lattice, model.Edges, GetSide(objects, sprite.Id), sprite.Position.X, sprite.Position.Y, DropReach));
            }
        }

        var byFamily = drops
            .Where(pair => pair.Key.Family is not null)
            .GroupBy(pair => pair.Key.Family.GetValueOrDefault())
            .ToDictionary(family => family.Key, family => family.SelectMany(pair => pair.Value).ToArray());

        float[] pickups = drops
            .Where(pair => pair.Key.PickupCategory is not null)
            .SelectMany(pair => pair.Value)
            .Order()
            .ToArray();

        var result = new Dictionary<KnownObject, (float, float)>();
        foreach (var id in Enum.GetValues<KnownObject>())
        {
            var familyKey = id.Family;
            if (id.PickupCategory is not null && pickups.Length >= MinSamples)
            {
                result[id] = (pickups[(int)(LowQuantile * pickups.Length)], GetSide(objects, id));
                continue;
            }

            if (familyKey is null)
                continue;

            var own = drops.GetValueOrDefault(id) ?? [];
            float[] family = byFamily.GetValueOrDefault(familyKey.Value) ?? [];
            float[] samples = own.Count >= MinDropSamples ? own.ToArray() : family;
            if (samples.Length >= MinDropSamples)
            {
                result[id] = (samples.Min(), GetSide(objects, id));
            }
        }

        return result;
    }

    /// <summary>
    /// Returns whether an object at a map point stands as far above the wall below it on screen as the originals stand its kind, at its middle and
    /// both sides; any point passes for a kind the originals hold too few of.
    /// </summary>
    public bool KeepsDrop(KnownObject id, Lattice lattice, IReadOnlyDictionary<EdgeKey, EdgeKind> edges, float x, float y) =>
        !_dropById.TryGetValue(id, out var rule) || MeasureDrop(lattice, edges, rule.Side, x, y, rule.Drop) >= rule.Drop;

    private static float GetSide(ObjectTable objects, KnownObject id) => objects.TryFind(id, out var obj) ? CollisionBox.ExtentShare * obj.Size.X : 0;

    private static float MeasureDrop(Lattice lattice, IReadOnlyDictionary<EdgeKey, EdgeKind> edges, float side, float x, float y, float reach) =>
        MathF.Min(edges.MeasureDrop(lattice, x, y, reach), MathF.Min(edges.MeasureDrop(lattice, x - side, y, reach), edges.MeasureDrop(lattice, x + side, y, reach)));

    /// <summary>
    /// Returns the closest any original puts an object to a wall, and the distance 95% of them keep, or false for an object the originals don't place.
    /// </summary>
    public bool TryGetClearance(KnownObject id, out float minimum, out float low)
    {
        bool found = _byId.TryGetValue(id, out var c);
        (minimum, low) = c;
        return found;
    }
}
