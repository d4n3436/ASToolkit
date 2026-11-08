using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Generation.Dressing;

namespace ASToolkit.Core.Generation.Content;

/// <summary>
/// Provides the set pieces read from the original levels, such as breaker and cannon scenes, door and missile marks, and dynamite sites.
/// </summary>
public static class OriginalContent
{
    private const float CannonSceneReach = 230;
    private const float DoorMarkReach = 60;
    private const float MissileReach = 120;
    private const float MissileMarkReach = 90;
    private const float WallSignHeight = 60;

    /// <summary>
    /// Returns the props within 170 pixels of each breaker of the given levels.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<PropPlacement>> LoadBreakerScenes(ReferenceLevelSet references, IEnumerable<string> levels)
    {
        var scenes = new List<IReadOnlyList<PropPlacement>>();
        foreach (string level in levels)
        {
            var sp = references.GetLevel(level).Map.Sprites;
            foreach (var breaker in sp.Where(s => s.Id == KnownObject.Breaker))
            {
                scenes.Add(sp.Where(o => o.Id is KnownObject.ObjectMarker or KnownObject.Shelves or KnownObject.Table or KnownObject.Cabinet or KnownObject.Transformer or KnownObject.SoldierCorpse or KnownObject.BloodPool or KnownObject.LargeBloodPool && MathF.Sqrt(MathF.Pow(o.Position.X - breaker.Position.X, 2) + MathF.Pow(o.Position.Y - breaker.Position.Y, 2)) < 170)
                    .Select(o => new PropPlacement(o.Id, o.Position.X - breaker.Position.X, o.Position.Y - breaker.Position.Y, o.Position.Z, o.Facing)).ToArray());
            }
        }

        return scenes.ToArray();
    }

    /// <summary>
    /// Returns the floor stripes (131) within 230 pixels of each mounted cannon of the given levels.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<PropPlacement>> LoadCannonScenes(ReferenceLevelSet references, IEnumerable<string> levels)
    {
        var scenes = new List<IReadOnlyList<PropPlacement>>();
        foreach (string level in levels)
        {
            var sp = references.GetLevel(level).Map.Sprites;
            foreach (var cannon in sp.Where(s => s.Id == KnownObject.MountedCannon))
            {
                scenes.Add(sp.Where(o => o.Id == KnownObject.WallInscriptions && MathF.Sqrt(MathF.Pow(o.Position.X - cannon.Position.X, 2) + MathF.Pow(o.Position.Y - cannon.Position.Y, 2)) < CannonSceneReach)
                    .Select(o => new PropPlacement(o.Id, o.Position.X - cannon.Position.X, o.Position.Y - cannon.Position.Y, o.Position.Z, o.Facing)).ToArray());
            }
        }

        return scenes.Where(scene => scene.Count > 0).ToArray();
    }

    /// <summary>
    /// Returns the floor stripes (131) within 90 pixels of each missile three of the given levels, placed from the middle missile and
    /// keyed by its facing, and the share of the threes that have any.
    /// </summary>
    public static (Dictionary<byte, List<IReadOnlyList<PropPlacement>>> Marks, float Share) LoadMissileMarks(ReferenceLevelSet references, IEnumerable<string> levels)
    {
        var marks = new Dictionary<byte, List<IReadOnlyList<PropPlacement>>>();
        int threes = 0;
        foreach (string level in levels)
        {
            var sp = references.GetLevel(level).Map.Sprites;
            var stripes = sp.Where(s => s is { Id: KnownObject.WallInscriptions, Position.Z: < WallSignHeight }).ToArray();
            var left = sp.Where(s => s.Id == KnownObject.Missile).ToList();
            while (left.Count > 0)
            {
                var group = new List<MergedMapSprite> { left[0] };
                left.RemoveAt(0);
                for (int index = 0; index < group.Count; index++)
                {
                    var joined = left.Where(s => GetDistance(s, group[index]) <= MissileReach).ToArray();
                    group.AddRange(joined);
                    left.RemoveAll(joined.Contains);
                }

                if (group.Count != 3)
                {
                    continue;
                }

                threes++;
                var middle = group.MinBy(s => group.Sum(other => GetDistance(s, other)))!;
                var near = stripes.Where(s => group.Any(missile => GetDistance(s, missile) <= MissileMarkReach))
                    .Select(o => new PropPlacement(o.Id, o.Position.X - middle.Position.X, o.Position.Y - middle.Position.Y, o.Position.Z, o.Facing)).ToArray();
                if (near.Length == 0)
                {
                    continue;
                }

                if (!marks.TryGetValue(middle.Facing, out var list))
                {
                    list = [];
                    marks[middle.Facing] = list;
                }

                list.Add(near);
            }
        }

        return (marks, marks.Values.Sum(list => list.Count) / (float)Math.Max(1, threes));
    }

    /// <summary>
    /// Returns the wall marks (131) within 60 pixels of each door of a map, keyed by the door's ID and facing, and the share of its doors that have any.
    /// </summary>
    public static (Dictionary<(KnownObject Id, byte Dir), List<IReadOnlyList<PropPlacement>>> Marks, float Share) LoadDoorMarks(MapResourceBuilder map)
    {
        var marks = new Dictionary<(KnownObject, byte), List<IReadOnlyList<PropPlacement>>>();
        var doors = map.Sprites.Where(s => s.Id.IsDoor || s.Id == KnownObject.FenceEmitter).ToArray();
        foreach (var door in doors)
        {
            var near = map.Sprites.Where(o => o.Id == KnownObject.WallInscriptions && MathF.Sqrt(MathF.Pow(o.Position.X - door.Position.X, 2) + MathF.Pow(o.Position.Y - door.Position.Y, 2)) < DoorMarkReach)
                .Select(o => new PropPlacement(o.Id, o.Position.X - door.Position.X, o.Position.Y - door.Position.Y, o.Position.Z, o.Facing)).ToArray();
            if (near.Length == 0)
            {
                continue;
            }

            if (!marks.TryGetValue((door.Id, door.Facing), out var list))
            {
                list = [];
                marks[(door.Id, door.Facing)] = list;
            }

            list.Add(near);
        }

        return (marks, marks.Values.Sum(list => list.Count) / (float)Math.Max(1, doors.Length));
    }

    /// <summary>
    /// Returns every dynamite site of the given levels with the pads and generators within 130 pixels of it.
    /// </summary>
    public static SiteTemplate[] LoadDynamiteSites(ReferenceLevelSet references, IEnumerable<string> levels, WallSet wallSet)
    {
        var sites = new List<SiteTemplate>();
        foreach (string level in levels)
        {
            var sp = references.GetLevel(level).Map.Sprites;
            foreach (var sprite in sp.Where(s => s.Id == KnownObject.DynamiteSite))
            {
                var members = sp
                    .Where(o => (o.Id is KnownObject.TeleportPad or KnownObject.BigMonsterHole or KnownObject.SmallMonsterHole || o.Id.IsEndlessGenerator) && MathF.Sqrt(MathF.Pow(o.Position.X - sprite.Position.X, 2) + MathF.Pow(o.Position.Y - sprite.Position.Y, 2)) < 130)
                    .Select(o => new SiteMember(o.Id, o.Position.X - sprite.Position.X, o.Position.Y - sprite.Position.Y, o.Position.Z, o.Facing, o.Army,
                        o.Payload.Length <= 12 ? o.Payload : new byte[o.Id == KnownObject.TeleportPad ? 12 : 8]))
                    .ToList();
                if (wallSet == WallSet.Basement)
                {
                    members.RemoveAll(m => m.Id is KnownObject.BigMonsterHole or KnownObject.SmallMonsterHole);
                }

                if (wallSet == WallSet.Concrete && members.All(m => m.Id != KnownObject.TeleportPad))
                {
                    continue;
                }

                int rank = members.Where(m => m.Id >= KnownObject.FirstEndlessGenerator).Select(m => m.Id - KnownObject.FirstEndlessGenerator).DefaultIfEmpty(0).Max();
                sites.Add(new SiteTemplate(sprite.Position.Z, sprite.Facing, rank, members, level));
            }
        }

        return sites.ToArray();
    }

    private static float GetDistance(MergedMapSprite a, MergedMapSprite b) => MathF.Sqrt(MathF.Pow(a.Position.X - b.Position.X, 2) + MathF.Pow(a.Position.Y - b.Position.Y, 2));
}
