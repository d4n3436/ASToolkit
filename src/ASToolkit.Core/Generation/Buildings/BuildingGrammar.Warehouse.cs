using System.Drawing;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Rooms;

namespace ASToolkit.Core.Generation.Buildings;

internal abstract partial class BuildingGrammar
{
    private const int WarehouseDraws = 8;
    private const int GeneratorReach = 4;

    protected void BuildWarehouse(int level)
    {
        var variants = WarehouseTemplates[Math.Clamp(level, 6, 9)];
        for (int draw = 0; draw < WarehouseDraws; draw++)
        {
            Count(BuildingTally.WarehouseDraws);
            var template = Random.Choice(variants);
            var areas = DrawAreas(template);
            if (areas is null)
            {
                continue;
            }

            var placed = ComposeAreas(template, areas);
            if (placed is not null)
            {
                WriteAreas(template, areas, placed);
                return;
            }
        }

        Count(BuildingTally.WarehouseUnsolved);
        throw new InvalidOperationException("Cannot lay the warehouse.");
    }

    private Dictionary<string, Area>? DrawAreas(IReadOnlyList<WarehouseArea> template)
    {
        var areas = new Dictionary<string, Area>();
        // Draw the A and B sides once per level, every area naming them sharing the draw
        (string a, string b) = Random.Next() < 0.5 ? ("MinY", "MaxY") : ("MaxY", "MinY");
        foreach (var spec in template)
        {
            Area? area = null;
            for (int attempt = 0; attempt < 30 && area is null; attempt++)
            {
                area = DrawArea(spec);
                if (area is not null && !area.Outline.IsConnected())
                {
                    area = null;
                }
            }

            if (area is null)
            {
                return null;
            }

            area.HostSide = spec.HostSide switch { "A" => a, "B" => b, var side => side };
            areas[spec.Name] = area;
        }

        return areas;
    }

    private Area? DrawArea(WarehouseArea spec)
    {
        int w = Random.Inclusive(spec.Width.Min, spec.Width.Max);
        int l = Random.Inclusive(spec.Length.Min, spec.Length.Max);
        var area = spec.Shape switch
        {
            WarehouseShape.Hall => HallArea(w, l, spec.Feature, spec.HasIsland),
            WarehouseShape.Strip => StripArea(w, l, spec.Ends),
            WarehouseShape.Leg => LegArea(w, l, spec.Ends),
            WarehouseShape.Small => SingleArea(Box(0, 0, w, l), spec.First),
            WarehouseShape.Pair => PairArea(w, l, spec),
            WarehouseShape.Pocket => PocketArea(w, l, spec),
            WarehouseShape.Cluster => ClusterArea(w, l, spec.Rooms),
            WarehouseShape.Block => BlockArea(w, l, spec),
            _ => SingleArea(DepotFloor(w, l, spec.Arms, spec.Pillars, spec.In is null && spec.Faces == "WENS" ? 0 : spec.DoorLengths.Max() + 1), spec.First)
        };

        if (area is null)
        {
            return null;
        }

        foreach (var room in area.Rooms)
        {
            room.Phase ??= spec.Phase;
            room.Role = room.Role == "" ? spec.Role : room.Role;
        }

        return area;
    }

    private Dictionary<string, List<HashSet<Cell>>>? ComposeAreas(IReadOnlyList<WarehouseArea> template, Dictionary<string, Area> areas)
    {
        var specs = template.Where(spec => areas.ContainsKey(spec.Name)).ToArray();
        var joins = specs
            .Where(spec => spec.WayBack is not null && areas.ContainsKey(spec.WayBack))
            .Select(spec => (spec.WayBack!, spec.Name))
            .Concat(specs.Where(spec => spec.Host is not null).Select(spec => (HostOf(spec, areas), spec.Name)))
            .ToList();

        var sides = new Dictionary<(string, string), string>();
        foreach (var spec in specs.Where(spec => spec.Host is not null))
        {
            string host = HostOf(spec, areas);
            if (spec.In is not null)
            {
                sides[(spec.Name, host)] = spec.In;
            }

            string? side = areas[spec.Name].HostSide;
            if (side is not null)
            {
                sides[(host, spec.Name)] = side;
            }
        }

        var touches = specs.SelectMany(spec => spec.Touch.Where(areas.ContainsKey).Select(other => (spec.Name, other))).ToList();
        var mayTouch = new HashSet<(string, string)>();
        if (specs.Any(spec => spec.Shape == WarehouseShape.Hall))
        {
            mayTouch.UnionWith(specs
                .SelectMany((spec, i) => specs.Skip(i + 1).Select(other => (spec.Name, other.Name)))
                .Where(pair => !touches.Contains(pair) && !touches.Contains((pair.Item2, pair.Item1))));
        }

        var doors = specs.ToDictionary(spec => spec.Name, spec => areas[spec.Name].Doors.Count > 0 ? areas[spec.Name].Doors : DoorsOf(areas[spec.Name], spec));
        var level = new LayoutProblem<string> { MinimumDistance = 2 };
        foreach (var spec in specs)
        {
            level.AddRoom(spec.Name, [new RoomShape(areas[spec.Name].Outline, doors[spec.Name], CanTurn(spec) ? RoomShape.AllTurns : [ShapeTurn.Identity])], spec.IsCorridor);
        }

        foreach ((string a, string b) in joins)
        {
            level.Join(a, b);
        }

        foreach (((string a, string b), string side) in sides)
        {
            level.DoorSides[(a, b)] = Enum.Parse<DoorSides>(side);
        }

        level.TouchPairs.AddRange(touches);
        level.MayTouchPairs.AddRange(mayTouch);
        var layout = new BlockSolver(Random).Solve(level);
        return layout?.ToDictionary(room => room.Key, room => Fit(areas[room.Key], room.Value));
    }

    private static bool CanTurn(WarehouseArea spec) =>
        !(spec.Shape is WarehouseShape.Hall or WarehouseShape.Strip or WarehouseShape.Leg || spec.In is not null || spec.Faces != "WENS");

    private static string HostOf(WarehouseArea spec, Dictionary<string, Area> areas) => spec.Host!.Split('|').First(areas.ContainsKey);

    // Returns doors on the outer walls of the area's entry room that face the template's ways, one for each join length
    private static List<ShapeDoor> DoorsOf(Area area, WarehouseArea spec)
    {
        var entry = area.DoorCells.Count > 0 ? area.DoorCells : area.Rooms[area.Entry].Cells;
        var units = new HashSet<(Cell From, Cell To, char Face)>();
        foreach (var cell in entry)
        {
            foreach ((int du, int dv, char face, var from, var to) in new[]
            {
                (-1, 0, 'W', cell, cell with { V = cell.V + 1 }), (1, 0, 'E', cell with { U = cell.U + 1 }, new Cell(cell.U + 1, cell.V + 1)),
                (0, -1, 'N', cell, cell with { U = cell.U + 1 }), (0, 1, 'S', cell with { V = cell.V + 1 }, new Cell(cell.U + 1, cell.V + 1))
            })
            {
                if (!area.Outline.Contains(new Cell(cell.U + du, cell.V + dv)) && spec.Faces.Contains(face))
                {
                    units.Add((from, to, face));
                }
            }
        }

        var doors = new HashSet<(Cell, Cell)>();
        foreach ((var from, var to, char face) in units)
        {
            (int du, int dv) = (to.U - from.U, to.V - from.V);
            foreach (int length in spec.DoorLengths)
            {
                if (Enumerable.Range(0, length).All(k => units.Contains((new Cell(from.U + du * k, from.V + dv * k), new Cell(from.U + du * (k + 1), from.V + dv * (k + 1)), face))))
                {
                    doors.Add((from, new Cell(from.U + du * length, from.V + dv * length)));
                }
            }
        }

        return doors.Select(door => new ShapeDoor(new Point(door.Item1.U, door.Item1.V), new Point(door.Item2.U, door.Item2.V))).ToList();
    }

    private void WriteAreas(IReadOnlyList<WarehouseArea> template, Dictionary<string, Area> areas, Dictionary<string, List<HashSet<Cell>>> placed)
    {
        var ids = new Dictionary<string, int[]>();
        var all = new List<int>();
        var open = new HashSet<(int, int)>();
        foreach (var spec in template.Where(spec => areas.ContainsKey(spec.Name)))
        {
            var area = areas[spec.Name];
            var rooms = placed[spec.Name];
            int group = NewGroup();
            ids[spec.Name] = rooms.Select((cells, i) =>
            {
                var room = area.Rooms[i];
                var rects = cells.ToRects();
                int id;
                if (room.Type == RoomType.Hall)
                {
                    var ordered = rects.OrderByDescending(rect => rect.W * rect.H).ToArray();
                    id = AddHall(ordered[0], room.Phase!.Value, ordered[0].W >= ordered[0].H);
                    foreach (var arm in ordered.Skip(1))
                    {
                        AddArm(id, arm, arm.W >= arm.H);
                    }
                }
                else
                {
                    id = AddDepot(rects, room.Type, room.Phase!.Value, group);
                }

                all.Add(id);
                return id;
            }).ToArray();

            foreach ((int a, int b) in area.Joins)
            {
                Join(ids[spec.Name][a], ids[spec.Name][b], open, false);
            }
        }

        foreach (var spec in template.Where(spec => areas.ContainsKey(spec.Name) && spec.Host is not null))
        {
            int[] host = ids[HostOf(spec, areas)];
            Join(host[areas[HostOf(spec, areas)].Entry], ids[spec.Name][areas[spec.Name].Entry], open, spec.IsWide);
        }

        foreach (var spec in template.Where(spec => areas.ContainsKey(spec.Name) && spec.WayBack is not null && areas.ContainsKey(spec.WayBack)))
        {
            (int a, int b) = (ids[spec.WayBack!][areas[spec.WayBack!].Entry], ids[spec.Name][areas[spec.Name].Entry]);
            if (Grid[a].Phase == Grid[b].Phase)
            {
                open.Add((a, b));
                Shortcuts.Add((a, b));
            }
            else
            {
                Join(a, b, open, false);
            }
        }

        CloseWing(all, open, new HashSet<int>());

        Start = Role("start");
        int generator = Role("generator");
        Keys.Add(generator);
        int end = Role("end");
        End = end >= 0 ? end : Role("arena");
        int gate = Locks.Select(pair => pair.To).DefaultIfEmpty(-1).First();
        GateRoom = gate >= 0 ? gate : null;
        return;

        int Role(string role) => template.Where(spec => areas.ContainsKey(spec.Name))
            .SelectMany(spec => areas[spec.Name].Rooms.Select((room, i) => (room.Role, Id: ids[spec.Name][i])))
            .Where(pair => pair.Role == role)
            .Select(pair => pair.Id)
            .DefaultIfEmpty(-1)
            .Last();
    }

    private void Join(int a, int b, HashSet<(int, int)> open, bool isWide)
    {
        if (AddDesignedDoor(a, b, open) && isWide)
        {
            OpenWalls.Add((a, b));
        }
    }

    private static List<HashSet<Cell>> Fit(Area area, PlacedRoom<string> placed)
    {
        var target = placed.Cells;

        var moved = area.Outline.Select(Turn).ToList();
        (int du, int dv) = (target.Min(c => c.U) - moved.Min(c => c.U), target.Min(c => c.V) - moved.Min(c => c.V));
        if (moved.Count != target.Count || !moved.All(c => target.Contains(new Cell(c.U + du, c.V + dv))))
        {
            throw new InvalidOperationException("Cannot fit the area into its placed outline.");
        }

        return area.Rooms.Select(room => room.Cells.Select(Turn).Select(c => new Cell(c.U + du, c.V + dv)).ToHashSet()).ToList();

        Cell Turn(Cell cell) => placed.Turn.Apply(cell);
    }

    // Draws a hall l long and w deep, widened into a cross over its middle third (feature 2) or given one bay on a side (feature 1), with a wall
    // island in its straight body
    private Area HallArea(int w, int l, int feature, bool hasIsland)
    {
        var cells = Box(0, 0, l, w);
        if (feature == 2 && l >= 12)
        {
            int m0 = Random.Inclusive(l / 4, l / 3), m1 = l - Random.Inclusive(l / 4, l / 3);
            (int top, int bottom) = Random.Choice([(2, 2), (2, 3), (3, 2), (0, 3), (3, 0)]);
            cells.UnionWith(Box(m0, -top, m1, w + bottom));
        }
        else if (feature == 1 && l >= 8 && Random.Next() < 0.9)
        {
            int bl = Random.Inclusive(4, Math.Min(7, l - 4)), bd = Random.Inclusive(2, 3);
            int b0 = l - bl - 2 >= 2 ? Random.Inclusive(2, l - bl - 2) : 0;
            cells.UnionWith(Random.Next() < 0.5 ? Box(b0, -bd, b0 + bl, 0) : Box(b0, w, b0 + bl, w + bd));
        }

        var outline = new HashSet<Cell>(cells);
        if (hasIsland && l >= 14 && w >= 6)
        {
            int iu = Random.Inclusive(4, l - 9);
            cells.ExceptWith(Box(iu, w / 2 - 1, iu + Random.Inclusive(4, 6), w / 2 + 1));
        }

        return new Area([new AreaRoom(cells, RoomType.Hall)], outline: outline);
    }

    private static Area StripArea(int w, int l, string ends)
    {
        var area = new Area([new AreaRoom(Box(0, 0, l, w), RoomType.Hall)]);
        if (ends.Contains('W'))
        {
            area.Doors.Add(new ShapeDoor(new Point(0, 0), new Point(0, w)));
        }

        if (ends.Contains('E'))
        {
            area.Doors.Add(new ShapeDoor(new Point(l, 0), new Point(l, w)));
        }

        return area;
    }

    // Lays a leg east, or north or south as its far end names, with the machine room beside its far end
    private Area LegArea(int w, int l, string end)
    {
        int side = Random.Inclusive(3, 4);
        bool isUpright = end is "N" or "S";
        int far = end == "N" ? 0 : l - side;
        var hall = isUpright ? Box(0, 0, w, l) : Box(0, 0, l, w);
        var machine = (isUpright, Random.Next() < 0.5) switch
        {
            (false, true) => Box(l - side, -side, l, 0),
            (false, false) => Box(l - side, w, l, w + side),
            (true, true) => Box(-side, far, 0, far + side),
            _ => Box(w, far, w + side, far + side)
        };

        var area = new Area([new AreaRoom(hall, RoomType.Hall) { Role = "route" }, new AreaRoom(machine, RoomType.MachineRoom) { Role = "generator" }]);
        area.Joins.Add((0, 1));
        area.Doors.Add(isUpright ? new ShapeDoor(new Point(0, 0), new Point(w, 0)) : new ShapeDoor(new Point(0, 0), new Point(0, w)));
        area.Doors.Add(isUpright ? new ShapeDoor(new Point(0, l), new Point(w, l)) : new ShapeDoor(new Point(l, 0), new Point(l, w)));
        return area;
    }

    private static Area SingleArea(HashSet<Cell> cells, RoomType type) => new([new AreaRoom(cells, type)], outline: cells.GetFilled());

    private Area? PairArea(int w, int d, WarehouseArea spec)
    {
        var parts = Partition(Box(0, 0, w, d), 2, true);
        parts = parts is null ? null : Walk(parts, part => part.Count(c => c.V == 0) >= 2, _ => true);
        if (parts is null)
        {
            return null;
        }

        parts = Interlock(parts, 2, 2);
        if (parts[0].Count(c => c.V == 0) < 2)
        {
            return null;
        }

        var area = new Area([new AreaRoom(parts[0], spec.First), new AreaRoom(parts[1], spec.Second) { Phase = spec.SecondPhase, Role = spec.SecondRole }]);
        area.Joins.Add((0, 1));
        return area;
    }

    private Area? PocketArea(int w, int d, WarehouseArea spec)
    {
        int rooms = Math.Min(spec.Rooms, Math.Max(1, d / 3));
        var parts = rooms > 1 ? Partition(Box(0, 0, w, d), rooms, spec.Interlocks) : [Box(0, 0, w, d)];
        parts = parts is null || rooms == 1 ? parts : Walk(parts, part => part.Any(c => c.V == 0), _ => true);
        if (parts is null)
        {
            return null;
        }

        parts = spec.Interlocks ? Interlock(parts, parts.Count, 2) : parts;
        if (parts[0].All(c => c.V != 0) || !IsChained(parts))
        {
            return null;
        }

        var area = new Area(parts.Select((part, i) => new AreaRoom(part, i == 0 ? spec.First : i == parts.Count - 1 && spec.Role == "generator" ? RoomType.MachineRoom : spec.Second)
        {
            Role = i == parts.Count - 1 ? spec.Role : "route"
        }).ToList());
        area.Joins.AddRange(Enumerable.Range(0, parts.Count - 1).Select(i => (i, i + 1)));
        return area;
    }

    private Area? ClusterArea(int w, int d, int nested)
    {
        int rw = Random.Inclusive(3, 4), rd = Random.Inclusive(3, 4);
        if (rw * nested > w - 3)
        {
            return null;
        }

        int corner = Random.Next() < 0.5 ? 0 : w - rw * nested;
        var nest = Enumerable.Range(0, nested).Select(k => Box(corner + k * rw, d - rd, corner + (k + 1) * rw, d)).ToList();
        var body = Box(0, 0, w, d);
        nest.ForEach(body.ExceptWith);
        var order = corner == 0 ? nest : Enumerable.Reverse(nest).ToList();
        var rooms = new List<AreaRoom> { new(body, RoomType.Depot) };
        rooms.AddRange(order.Select((cells, k) => new AreaRoom(cells, k == nested - 1 ? RoomType.MachineRoom : RoomType.Storage) { Role = k == nested - 1 ? "generator" : "route" }));
        var area = new Area(rooms);
        area.Joins.AddRange(Enumerable.Range(0, nested).Select(k => (k, k + 1)));
        var exit = order[0];
        area.DoorCells.UnionWith(body.Where(cell => exit.Any(other => Math.Max(Math.Abs(cell.U - other.U), Math.Abs(cell.V - other.V)) <= GeneratorReach)));
        return area;
    }

    private Area? BlockArea(int w, int d, WarehouseArea spec)
    {
        int strip = Random.Inclusive(2, 3);
        var body = Box(0, strip, w, d);
        var row = Partition(body, spec.Rooms - 1, true);
        int top = d - 1;
        row = row is null ? null : Walk(row, part => part.Any(c => c.V == strip), part => part.Count(c => c.V == top) >= 4);
        if (row is null)
        {
            return null;
        }

        row = Interlock(row, row.Count, 3);
        if (!IsChained(row) || row[0].All(c => c.V != strip) || row[^1].Count(c => c.V == top) < 4)
        {
            return null;
        }

        var rooms = new List<AreaRoom> { new(Box(0, 0, w, strip), RoomType.Passage) { Role = "route" } };
        rooms.AddRange(row.Select(part => new AreaRoom(part, Random.Next() < 0.6 ? RoomType.Depot : RoomType.Storage) { Role = "route" }));
        int[] edge = row[^1].Where(c => c.V == top).Select(c => c.U).Order().ToArray();
        int hw = Math.Min(Random.Inclusive(4, 6), edge.Length);
        int hl = Random.Inclusive(14, 18);
        int u0 = Random.Inclusive(edge[0], Math.Max(edge[0], edge[^1] + 1 - hw));
        var hall = Box(u0, top + 1, u0 + hw, top + 1 + hl);
        if (Random.Next() < 0.9)
        {
            int bl = Random.Inclusive(4, 7), bd = Random.Inclusive(2, 3), b0 = top + 1 + Random.Inclusive(2, hl - bl - 2);
            hall.UnionWith(Random.Next() < 0.5 ? Box(u0 - bd, b0, u0, b0 + bl) : Box(u0 + hw, b0, u0 + hw + bd, b0 + bl));
        }

        int endTop = top + 1 + hl;
        rooms.Add(new AreaRoom(hall, RoomType.Hall) { Role = "arena" });
        rooms.Add(new AreaRoom(Box(u0, endTop, u0 + Random.Inclusive(3, 4), endTop + Random.Inclusive(3, 4)), RoomType.Storage) { Role = "end" });
        var area = new Area(rooms);
        area.Joins.AddRange(Enumerable.Range(0, rooms.Count - 1).Select(i => (i, i + 1)));
        return area;
    }

    // Draws a depot floor as a box, an L, a T or an I, keeping `keep` pieces of its west and east walls straight for doors, with two-by-two pillars
    // inside
    private HashSet<Cell> DepotFloor(int w, int d, int arms, int pillars, int keep)
    {
        string shape = arms == 0 ? Random.Choice(["box", "L"]) : Random.Choice(["L", "T", "I"]);
        int band = Math.Max(3, d * 2 / 5);
        if (shape == "I" && (d - band) / 2 < keep)
        {
            shape = "T";
        }

        var cells = Box(0, 0, w, d);
        int b = Math.Min(Random.Inclusive(d / 3, d / 2), d - keep);
        if (shape == "L" && b >= 2)
        {
            int a = Random.Inclusive(w / 3, w / 2);
            int cu = Random.Next() < 0.5 ? 0 : w - a;
            cells.ExceptWith(Box(cu, d - b, cu + a, d));
        }
        else if (shape == "T")
        {
            int sw = Math.Max(3, w / 3), sl = Math.Max(3, d / 2), su = (w - sw) / 2;
            cells.UnionWith(Box(su, d, su + sw, d + sl));
        }
        else if (shape == "I")
        {
            int cut = Math.Max(2, w / 4), v0 = (d - band) / 2 + 1;
            cells.ExceptWith(Box(0, v0, cut, v0 + band));
            cells.ExceptWith(Box(w - cut, v0, w, v0 + band));
        }

        for (int pillar = 0; pillar < pillars; pillar++)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                int pu = Random.Inclusive(3, Math.Max(3, w - 5)), pv = Random.Inclusive(3, Math.Max(3, d - 5));
                var hole = Box(pu, pv, pu + 2, pv + 2);
                if (hole.All(cell => Box(cell.U - 2, cell.V - 2, cell.U + 3, cell.V + 3).All(cells.Contains)))
                {
                    cells.ExceptWith(hole);
                    break;
                }
            }
        }

        return cells;
    }

    // Divides a region into parts of uneven size, each cut across the longest part's longer side, deep cuts stepping and cutting corner boxes out
    private List<HashSet<Cell>>? Partition(HashSet<Cell> cells, int parts, bool deep)
    {
        const int least = 3;
        for (int attempt = 0; attempt < 40; attempt++)
        {
            var output = new List<HashSet<Cell>> { new(cells) };
            while (output.Count < parts)
            {
                var big = !deep || Random.Next() < 0.6 || output.Count < 2 ? output.MaxBy(part => part.Count)! : output.OrderBy(part => part.Count).ElementAt(output.Count - 2);
                (int u0, int v0, int u1, int v1) = (big.Min(c => c.U), big.Min(c => c.V), big.Max(c => c.U) + 1, big.Max(c => c.V) + 1);
                bool alongU = u1 - u0 >= v1 - v0;
                (int lo, int hi, int olo, int ohi) = alongU ? (u0, u1, v0, v1) : (v0, v1, u0, u1);
                if (hi - lo < 2 * least)
                {
                    break;
                }

                HashSet<Cell> piece;
                if (deep && ohi - olo >= 2 * least && Random.Next() < 0.45)
                {
                    int bw = Random.Inclusive(least, Math.Max(least, u1 - u0 - least)), bh = Random.Inclusive(least, Math.Max(least, v1 - v0 - least));
                    int cu = Random.Next() < 0.5 ? u0 : u1 - bw, cv = Random.Next() < 0.5 ? v0 : v1 - bh;
                    piece = big.Where(c => c.U >= cu && c.U < cu + bw && c.V >= cv && c.V < cv + bh).ToHashSet();
                }
                else
                {
                    double share = deep ? 0.25 : 0.35;
                    int cut = Random.Inclusive(lo + Math.Max(least, (int)((hi - lo) * share)), hi - Math.Max(least, (int)((hi - lo) * share)));
                    int most = deep ? Math.Max(2, (hi - lo) / 3) : 3;
                    (int at, int shift) = Random.Next() < (deep ? 0.9 : 0.75) && ohi - olo >= 5
                        ? (Random.Inclusive(olo + 2, ohi - 2), (Random.Next() < 0.5 ? -1 : 1) * Random.Inclusive(2, most))
                        : (ohi, 0);
                    piece = big.Where(c => (alongU ? c.U : c.V) < cut + ((alongU ? c.V : c.U) >= at ? shift : 0)).ToHashSet();
                }

                output.Remove(big);
                output.Add(piece);
                output.Add(big.Except(piece).ToHashSet());
            }

            if (output.Count == parts && output.All(part => part.Count >= least * least && part.IsThick(least) && part.IsConnected()))
            {
                return output;
            }
        }

        return null;
    }

    // Trades tabs one to `deepest` pieces deep across shared walls, keeping both rooms connected and two wide and the group's outline unchanged
    private List<HashSet<Cell>> Interlock(List<HashSet<Cell>> rooms, int trades, int deepest)
    {
        for (int attempt = 0; attempt < trades * 8 && trades > 0; attempt++)
        {
            var pairs = Enumerable.Range(0, rooms.Count)
                .SelectMany(a => Enumerable.Range(0, rooms.Count).Where(b => b != a && Touching(rooms[a], rooms[b]) >= 4).Select(b => (a, b)))
                .ToList();

            if (pairs.Count == 0)
            {
                break;
            }

            (int ia, int ib) = Random.Choice(pairs);
            var wall = rooms[ia].SelectMany(c => Steps.Where(step => rooms[ib].Contains(new Cell(c.U + step.U, c.V + step.V))).Select(step => (Cell: c, Step: step))).ToList();
            (var cell, (int du, int dv)) = Random.Choice(wall);
            int depth = Random.Inclusive(1, deepest), span = Random.Inclusive(2, 4);
            (int au, int av) = du != 0 ? (0, 1) : (1, 0);
            foreach (int start in Random.Shuffled(Enumerable.Range(-span + 1, span)))
            {
                var tab = Enumerable.Range(1, depth)
                    .SelectMany(k => Enumerable.Range(0, span).Select(j => new Cell(cell.U + du * k + au * (j + start), cell.V + dv * k + av * (j + start))))
                    .ToHashSet();

                if (!tab.IsSubsetOf(rooms[ib]))
                {
                    continue;
                }

                var rest = rooms[ib].Except(tab).ToHashSet();
                var grown = rooms[ia].Union(tab).ToHashSet();
                if (rest.Count >= 6 && rest.IsConnected() && rest.IsThick(2) && grown.IsThick(2))
                {
                    (rooms[ia], rooms[ib]) = (grown, rest);
                    trades--;
                    break;
                }
            }
        }

        return rooms;
    }

    private static List<HashSet<Cell>>? Walk(List<HashSet<Cell>> parts, Func<HashSet<Cell>, bool> first, Func<HashSet<Cell>, bool> last)
    {
        foreach (int start in Enumerable.Range(0, parts.Count).Where(i => first(parts[i])))
        {
            var order = Extend([start]);
            if (order is not null)
            {
                return order.Select(i => parts[i]).ToList();
            }
        }

        return null;

        List<int>? Extend(List<int> path)
        {
            if (path.Count == parts.Count)
            {
                return last(parts[path[^1]]) ? path : null;
            }

            foreach (int next in Enumerable.Range(0, parts.Count).Where(next => !path.Contains(next) && Touching(parts[path[^1]], parts[next]) > 0))
            {
                var found = Extend(path.Append(next).ToList());
                if (found is not null)
                {
                    return found;
                }
            }

            return null;
        }
    }

    private static bool IsChained(List<HashSet<Cell>> parts) => parts.Zip(parts.Skip(1)).All(pair => Touching(pair.First, pair.Second) > 0);

    private static int Touching(HashSet<Cell> a, HashSet<Cell> b) => a.Sum(c => Steps.Count(step => b.Contains(new Cell(c.U + step.U, c.V + step.V))));

    private static readonly (int U, int V)[] Steps = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    private static HashSet<Cell> Box(int u0, int v0, int u1, int v1) => new UnitRect(u0, v0, u1 - u0, v1 - v0).ToCells();

    private sealed class AreaRoom
    {
        public AreaRoom(HashSet<Cell> cells, RoomType type)
        {
            Cells = cells;
            Type = type;
        }

        public HashSet<Cell> Cells { get; }

        public RoomType Type { get; }

        public int? Phase { get; set; }

        public string Role { get; set; } = "";
    }

    private sealed class Area
    {
        public Area(List<AreaRoom> rooms, HashSet<Cell>? outline = null)
        {
            Rooms = rooms;
            Outline = outline ?? rooms.SelectMany(room => room.Cells).ToHashSet().GetFilled();
        }

        public List<AreaRoom> Rooms { get; }

        public HashSet<Cell> Outline { get; }

        public List<(int A, int B)> Joins { get; } = [];

        public List<ShapeDoor> Doors { get; } = [];

        public HashSet<Cell> DoorCells { get; } = [];

        public int Entry => 0;

        public string? HostSide { get; set; }
    }
}

internal enum WarehouseShape
{
    Hall,
    Strip,
    Leg,
    Small,
    Pair,
    Pocket,
    Cluster,
    Block,
    Depot
}

// Holds one area of a warehouse template, its shape, sizes in wall pieces, phase and role, the host it joins with the join's door sides (A and B
// drawn per level, In on the area), its way back from the key, the ways its doors face and the areas it must touch
internal sealed record WarehouseArea(string Name, WarehouseShape Shape, (int Min, int Max) Width, (int Min, int Max) Length)
{
    public int Phase { get; init; }

    public string Role { get; init; } = "";

    public string? Host { get; init; }

    public string? HostSide { get; init; }

    public string? In { get; init; }

    public string Faces { get; init; } = "WENS";

    public string Ends { get; init; } = "";

    public int[] DoorLengths { get; init; } = [2];

    public bool IsCorridor { get; init; }

    public bool IsWide { get; init; }

    public string? WayBack { get; init; }

    public string[] Touch { get; init; } = [];

    public RoomType First { get; init; } = RoomType.Depot;

    public RoomType Second { get; init; } = RoomType.Storage;

    public int SecondPhase { get; init; }

    public string SecondRole { get; init; } = "";

    public int Rooms { get; init; } = 1;

    public bool Interlocks { get; init; } = true;

    public int Arms { get; init; }

    public int Pillars { get; init; }

    public int Feature { get; init; }

    public bool HasIsland { get; init; }
}
