using System.Drawing;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Campaign;
using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;

namespace ASToolkit.Core.Generation.Content;

internal sealed class FightPlanner
{
    private const float MostVariantShare = 0.75f;
    private const float OpeningShare = 0.5f;
    private const float ScreenArea = 1280 * 720;
    private const float FightReach = 1000;

    private readonly EncounterModel _model;
    private readonly ObjectTable _objects;
    private readonly LayoutPlan _plan;
    private readonly LevelBrief _brief;
    private readonly Random _rng;
    private KnownObject? _pending;

    public FightPlanner(EncounterModel model, ObjectTable objects, LayoutPlan plan, LevelBrief brief, Random rng)
    {
        _model = model;
        _objects = objects;
        _plan = plan;
        _brief = brief;
        _rng = rng;
    }

    public IReadOnlyList<PlannedFight> Plan(int hitPoints)
    {
        var rooms = _plan.Regions
            .Where(region => region.Kind is RegionKind.Station or RegionKind.SideRoom or RegionKind.Pen && _plan.HoldsFights(region) && region != _plan.Stations[0])
            .OrderBy(region => region.Order)
            .ToArray();

        var monsters = _brief.Monsters;
        if (rooms.Length == 0 || monsters.Count == 0)
        {
            return [];
        }

        float area = rooms.Sum(room => room.Units.Count);
        var doors = _plan.Links
            .Where(link => link.IsChoke && link != _plan.BlackoutLink)
            .ToLookup(link => _plan.Regions[link.From].Order > _plan.Regions[link.To].Order ? link.From : link.To);

        float floor = _plan.Regions.Sum(region => region.Units.Count) * GetUnitArea() / ScreenArea;
        int count = Math.Clamp((int)MathF.Round(_model.FightsPerScreen.Value * floor), 1, rooms.Length);
        var chosen = new List<int>();
        var climaxes = new List<int>();
        float[] targets = new float[rooms.Length];
        int[][] phases = Enumerable.Range(0, rooms.Length)
            .GroupBy(index => rooms[index].Phase)
            .Select(phase => phase.ToArray())
            .ToArray();

        float[] weights = phases.Select(members => members.Sum(index => rooms[index].Units.Count) * (members.Any(index => doors.Contains(rooms[index].Id)) ? 1 : 1 - _brief.SpawnedShare)).ToArray();
        for (int phaseIndex = 0; phaseIndex < phases.Length; phaseIndex++)
        {
            int[] members = phases[phaseIndex];
            float phaseArea = members.Sum(index => rooms[index].Units.Count);
            float phaseHitPoints = hitPoints * weights[phaseIndex] / Math.Max(1, weights.Sum());
            int phaseFights = Math.Clamp((int)MathF.Round(count * phaseArea / area), 1, members.Length);
            float before = 0;
            var positions = new Dictionary<int, float>();
            foreach (int index in members)
            {
                positions[index] = before / phaseArea;
                before += rooms[index].Units.Count;
            }

            var picked = new List<int>();
            var slots = Enumerable.Range(0, phaseFights).Select(slot => (slot + 0.5f) / phaseFights).ToList();
            if (phaseFights > 1)
            {
                var drawn = _model.ClimaxPositions.Value;
                float target = drawn[_rng.Next(drawn.Count)];
                var chokes = members.Where(index => doors.Contains(rooms[index].Id)).ToArray();
                picked.Add((chokes.Length > 0 ? chokes : members).MinBy(index => Math.Abs(positions[index] - target)));
                slots.Remove(slots.MinBy(slot => Math.Abs(slot - target)));
                climaxes.Add(picked[0]);
            }

            foreach (float slot in slots)
            {
                picked.Add(members.Where(index => !picked.Contains(index)).MinBy(index => Math.Abs(positions[index] - slot)));
            }

            float restArea = picked.Skip(climaxes.Contains(picked[0]) ? 1 : 0).Sum(index => rooms[index].Units.Count);
            float rest = phaseHitPoints * (phaseFights > 1 ? 1 - _model.ClimaxShare.Value : 1);
            foreach (int index in picked)
            {
                targets[index] = climaxes.Contains(index) ? phaseHitPoints * _model.ClimaxShare.Value : rest * rooms[index].Units.Count / restArea;
            }

            chosen.AddRange(picked);
        }

        chosen.Sort();
        var newKinds = _brief.NewMonsters;
        int[] openings = chosen
            .Where(index => !climaxes.Contains(index))
            .Take(newKinds.Count)
            .ToArray();

        float cap = OpeningShare * hitPoints / chosen.Count;
        int[] sinks = chosen.Except(openings).ToArray();
        foreach (int opening in openings.Where(index => targets[index] > cap && sinks.Length > 0))
        {
            int sink = sinks.MaxBy(index => ((rooms[index].Phase == rooms[opening].Phase ? 4 : 0) + (climaxes.Contains(index) ? 0 : 2)) * hitPoints + targets[index]);
            targets[sink] += targets[opening] - cap;
            targets[opening] = cap;
        }

        var ambushes = new HashSet<int>();
        float spawned = 0;
        foreach (int index in climaxes.Concat(chosen.Except(climaxes).OrderBy(_ => _rng.Next())).Where(index => doors.Contains(rooms[index].Id) && !openings.Contains(index)))
        {
            int at = chosen.IndexOf(index);
            var door = GetPoint(doors[rooms[index].Id].First());
            bool together = new[] { at - 1, at + 1 }
                .Where(neighbor => neighbor >= 0 && neighbor < chosen.Count && ambushes.Contains(chosen[neighbor]))
                .Any(neighbor => GetDistance(GetPoint(doors[rooms[chosen[neighbor]].Id].First()), door) <= FightReach);

            if (together || !(spawned + targets[index] / 2 <= _brief.SpawnedShare * hitPoints))
                continue;
            
            ambushes.Add(index);
            spawned += targets[index];
        }

        var opensAt = newKinds
            .Select((kind, order) => KeyValuePair.Create(kind, order < openings.Length ? openings[order] : int.MaxValue))
            .ToDictionary();

        var fights = new List<PlannedFight>();
        float carry = 0;
        foreach (int index in chosen)
        {
            var room = rooms[index];
            int opening = Array.IndexOf(openings, index);
            var offered = monsters.Where(pair => opensAt.GetValueOrDefault(pair.Key, -1) <= index).ToDictionary();
            float target = targets[index] + (opening >= 0 ? 0 : carry);
            var group = DrawGroup(offered, target, opening >= 0 ? newKinds[opening] : null);
            int fightHitPoints = group.Sum(GetHitPoints);
            carry = (opening >= 0 ? carry : 0) + target - fightHitPoints;
            if (group.Count == 0)
            {
                continue;
            }

            if (!ambushes.Contains(index))
            {
                fights.Add(new PlannedFight(room.Order, room.Id, fightHitPoints, [group],
                    rooms.Where(other => other.Phase == room.Phase).OrderBy(other => Math.Abs(other.Order - room.Order)).Select(other => other.Id).ToArray(), null, null));
                continue;
            }

            var closet = _plan.Regions.FirstOrDefault(region => region.Kind == RegionKind.SpawnCloset && region.Host == room.Id && _plan.HoldsFights(region));
            int[] spawnerRooms = closet is not null ? [closet.Id] : rooms.Where(other => other.Order >= room.Order && other.Phase >= room.Phase).Select(other => other.Id).ToArray();
            fights.Add(new PlannedFight(room.Order, room.Id, fightHitPoints, Split(group), spawnerRooms, new FightTrigger(room.Id, doors[room.Id].First().Id), closet?.Id));
        }

        return fights;
    }

    private List<KnownObject> DrawGroup(Dictionary<KnownObject, float> offered, float target, KnownObject? forced)
    {
        var group = forced is null ? new List<KnownObject>() : [forced.Value];
        if (offered.Count == 0)
        {
            return group;
        }

        var family = forced?.MonsterFamily ?? _pending ?? DrawWeighted(offered
            .GroupBy(pair => pair.Key.MonsterFamily)
            .ToDictionary(variants => variants.Key, variants => variants.Sum(pair => pair.Value * GetHitPoints(pair.Key))));

        var variants = offered.Where(pair => pair.Key.MonsterFamily == family).ToArray();
        int[] hitPoints = variants.Select(pair => GetHitPoints(pair.Key)).ToArray();
        int[] counts = variants.Select(pair => group.Count(id => id == pair.Key)).ToArray();
        float total = group.Sum(GetHitPoints);
        while (true)
        {
            int drawn = DrawVariant(variants, counts, group.Count);
            if (total + hitPoints[drawn] / 2f > target)
            {
                _pending = group.Count == 0 ? family : null;
                return group;
            }

            group.Add(variants[drawn].Key);
            counts[drawn]++;
            total += hitPoints[drawn];
        }
    }

    private int DrawVariant(KeyValuePair<KnownObject, float>[] variants, int[] counts, int size)
    {
        bool anyOpen = false;
        for (int index = 0; index < variants.Length; index++)
        {
            anyOpen |= counts[index] < MostVariantShare * (size + 1);
        }

        float sum = 0;
        int last = 0;
        for (int index = 0; index < variants.Length; index++)
        {
            if (anyOpen && !(counts[index] < MostVariantShare * (size + 1)))
                continue;
            
            sum += variants[index].Value;
            last = index;
        }

        float roll = _rng.NextSingle() * sum;
        for (int index = 0; index < variants.Length; index++)
        {
            if (anyOpen && !(counts[index] < MostVariantShare * (size + 1)))
                continue;
            
            roll -= variants[index].Value;
            if (roll <= 0)
            {
                return index;
            }
        }

        return last;
    }

    private PointF GetPoint(PlanLink link)
    {
        var step = _plan.Settings.WallSet == WallSet.Basement ? Lattice.BasementStep : Lattice.ConcreteStep;
        var lattice = new Lattice(step.X * _plan.Settings.UnitCells, step.Y * _plan.Settings.UnitCells);
        var points = link.Edges.Select(edge => lattice.ToWorld((edge.Cells.First.U + edge.Cells.Second.U) / 2f + 0.5f, (edge.Cells.First.V + edge.Cells.Second.V) / 2f + 0.5f)).ToArray();
        return new PointF(points.Average(point => point.X), points.Average(point => point.Y));
    }

    private static float GetDistance(PointF first, PointF second) => MathF.Sqrt((first.X - second.X) * (first.X - second.X) + (first.Y - second.Y) * (first.Y - second.Y));

    private float GetUnitArea()
    {
        var step = _plan.Settings.WallSet == WallSet.Basement ? Lattice.BasementStep : Lattice.ConcreteStep;
        return 2 * step.X * step.Y * _plan.Settings.UnitCells * _plan.Settings.UnitCells;
    }

    private List<KnownObject>[] Split(List<KnownObject> group)
    {
        var sizes = _model.SpawnerSizes.Value;
        var spawners = new List<List<KnownObject>>();
        for (int taken = 0; taken < group.Count; taken += spawners[^1].Count)
        {
            spawners.Add(group.Skip(taken).Take(sizes[_rng.Next(sizes.Count)]).ToList());
        }

        return spawners.ToArray();
    }

    private KnownObject DrawWeighted(Dictionary<KnownObject, float> weights)
    {
        float roll = _rng.NextSingle() * weights.Values.Sum();
        foreach ((var kind, float weight) in weights)
        {
            roll -= weight;
            if (roll <= 0)
            {
                return kind;
            }
        }

        return weights.Keys.Last();
    }

    private int GetHitPoints(KnownObject id) => _objects.TryFind(id, out var found) ? found.MaxHp : 0;
}
