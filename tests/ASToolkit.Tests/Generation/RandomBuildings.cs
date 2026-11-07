using ASToolkit.Core.Generation.Buildings;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Rooms;
using ASToolkit.Core.Survey;

namespace ASToolkit.Tests.Generation;

internal static class RandomBuildings
{
    public static BuildingPlan Create(Random rng, LevelStyle gate)
    {
        int width = rng.Next(3, 7);
        int height = rng.Next(3, 6);
        var contacts = new List<(int A, int B)>();
        for (int v = 0; v < height; v++)
        {
            for (int u = 0; u < width; u++)
            {
                if (u + 1 < width && rng.NextDouble() < 0.85)
                {
                    contacts.Add((Id(u, v), Id(u + 1, v)));
                }

                if (v + 1 < height && rng.NextDouble() < 0.85)
                {
                    contacts.Add((Id(u, v), Id(u, v + 1)));
                }
            }
        }

        int count = width * height;
        var neighbors = Enumerable.Range(0, count).ToDictionary(room => room, _ => new List<int>());
        foreach ((int a, int b) in contacts)
        {
            neighbors[a].Add(b);
            neighbors[b].Add(a);
        }

        const int start = 0;
        var reached = Grow(neighbors, [start], Enumerable.Range(0, count).ToHashSet(), count);
        if (reached.Count < 6)
        {
            return Create(rng, gate);
        }

        var phaseOf = new Dictionary<int, int>();
        var locks = new List<(int From, int To)>();
        int[] keys = [];
        int? trigger = null;
        if (gate == LevelStyle.Ungated)
        {
            foreach (int room in reached)
            {
                phaseOf[room] = 0;
            }
        }
        else if (gate == LevelStyle.Blackout)
        {
            var quiet = Grow(neighbors, [start], reached, Math.Max(2, reached.Count / 4), rng);

            var rest = reached
                .Except(quiet)
                .ToHashSet();

            int[] doorways = rest
                .Where(room => neighbors[room].Any(quiet.Contains))
                .Order()
                .ToArray();

            if (doorways.Length == 0)
            {
                return Create(rng, gate);
            }

            trigger = doorways[rng.Next(doorways.Length)];
            var dark = Grow(neighbors, [trigger.Value], rest, Math.Max(3, rest.Count / 2), rng);

            int[] breakers = dark
                .Where(room => room != trigger)
                .Order()
                .ToArray();

            if (breakers.Length == 0)
            {
                return Create(rng, gate);
            }

            foreach (int room in quiet)
            {
                phaseOf[room] = 0;
            }

            foreach (int room in dark)
            {
                phaseOf[room] = 1;
            }

            foreach (var piece in FindPieces(neighbors, rest.Except(dark).ToHashSet()))
            {
                int[] sources = dark
                    .Where(room => neighbors[room].Any(piece.Contains))
                    .Order()
                    .ToArray();

                if (sources.Length == 0)
                {
                    foreach (int room in piece)
                    {
                        phaseOf[room] = 0;
                    }

                    continue;
                }

                int source = sources[rng.Next(sources.Length)];

                int target = neighbors[source]
                    .Where(piece.Contains)
                    .Min();

                locks.Add((source, target));
                foreach (int room in piece)
                {
                    phaseOf[room] = 2;
                }
            }

            keys = [breakers[rng.Next(breakers.Length)]];
        }
        else
        {
            var first = Grow(neighbors, [start], reached, Math.Max(3, reached.Count / 2), rng);
            foreach (int room in first)
            {
                phaseOf[room] = 0;
            }

            foreach (var piece in FindPieces(neighbors, reached.Except(first).ToHashSet()))
            {
                int[] sources = first
                    .Where(room => neighbors[room].Any(piece.Contains))
                    .Order()
                    .ToArray();

                int source = sources[rng.Next(sources.Length)];
                locks.Add((source, neighbors[source].Where(piece.Contains).Min()));
                foreach (int room in piece)
                {
                    phaseOf[room] = 1;
                }
            }

            int[] candidates = first
                .Where(room => room != start)
                .Order()
                .ToArray();

            keys = candidates
                .OrderBy(_ => rng.Next())
                .Take(gate == LevelStyle.Fenced ? rng.Next(1, 4) : 1)
                .ToArray();
        }

        if ((gate != LevelStyle.Ungated && !phaseOf.ContainsValue(1)) || (gate == LevelStyle.Blackout && !phaseOf.ContainsValue(2)))
        {
            return Create(rng, gate);
        }

        var rooms = Enumerable.Range(0, count).Select(room => new BuildingRoom
        {
            Id = room,
            Kind = !phaseOf.ContainsKey(room) ? BuildingRoomKind.SecretCloset : rng.NextDouble() < 0.15 ? BuildingRoomKind.Hall : BuildingRoomKind.Room,
            Type = RoomType.Office,
            Rects = [new UnitRect(room % width * 4, room / width * 4, 4, 4)],
            Necks = [],
            Phase = phaseOf.GetValueOrDefault(room)
        }).ToArray();

        return new BuildingPlan
        {
            Gate = gate,
            HasSites = rng.NextDouble() < 0.5,
            Rooms = rooms,
            Contacts = contacts.Select(pair => new RoomPair(pair.A, pair.B)).ToArray(),
            Start = start,
            Keys = keys,
            Trigger = trigger,
            GateRoom = locks.Count > 0 ? locks[0].To : null,
            Locks = locks,
            Shortcuts = []
        };
        
        int Id(int u, int v) => v * width + u;
    }

    private static HashSet<int> Grow(Dictionary<int, List<int>> neighbors, IEnumerable<int> seeds, IReadOnlySet<int> within, int size, Random? rng = null)
    {
        var grown = seeds.ToHashSet();
        var frontier = new List<int>();
        foreach (int seed in grown)
        {
            frontier.AddRange(neighbors[seed].Where(within.Contains));
        }

        while (grown.Count < size && frontier.Count > 0)
        {
            int index = rng?.Next(frontier.Count) ?? 0;
            int room = frontier[index];
            frontier.RemoveAt(index);
            if (grown.Add(room))
            {
                frontier.AddRange(neighbors[room].Where(next => within.Contains(next) && !grown.Contains(next)));
            }
        }

        return grown;
    }

    private static List<HashSet<int>> FindPieces(Dictionary<int, List<int>> neighbors, HashSet<int> rooms)
    {
        var pieces = new List<HashSet<int>>();
        var left = rooms.ToHashSet();
        while (left.Count > 0)
        {
            var piece = Grow(neighbors, [left.Min()], left, left.Count);
            pieces.Add(piece);
            left.ExceptWith(piece);
        }

        return pieces;
    }

    private static (int, int) Order(int a, int b) => a < b ? (a, b) : (b, a);
}
