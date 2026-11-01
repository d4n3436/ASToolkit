using System.Drawing;
using System.Numerics;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Geometry;
using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Reference;

namespace ASToolkit.Core.Generation.Outdoor;

/// <summary>
/// Provides the building of an outdoor level 1: the original's ground, roofed buildings and chain-link fences, with the content of one
/// of the three originals.
/// </summary>
public sealed class OutdoorLevelBuilder
{
    private const float Margin = 120;
    private const int FenceCells = 2;
    private const int BuildingGap = 2;
    private const int CompoundExitRadius = 40;
    private const float MinExitLine = 1300;
    private const int CompoundTries = 8;
    private const float ChargeLow = 900;
    private const float ChargeHigh = 1600;
    private const float MonsterSpacing = 22;
    private const int ClusterSlack = 4;
    private const float SpawnerTriggerRadius = 300;
    private const float BatSpacing = 30;
    private const float RoofHeight = 90;
    private const float BreachReach = 130;
    private const float ClusterReach = 80;
    private const float RoadHalfWidth = 90;
    private const float RoadOvershoot = 700;
    private const float BorderGrass = 140;
    private const float WallGrass = 1.3f;
    private const float ClumpStepX = 50;
    private const float ClumpStepY = 48;
    private const float ClumpJitter = 8;
    private const float TuftStep = 25;
    private const float TuftReach = 30;
    private const float TuftChance = 0.6f;
    private const int LampSpacing = 3;
    private const int OpeningClearance = 2;
    private const int MinRowTiles = 3;

    private static readonly HashSet<KnownObject> YardProps = [KnownObject.Crate, KnownObject.Container, KnownObject.Barrel, KnownObject.Cylinder, KnownObject.Truck, KnownObject.Container2Alt];
    private static readonly int[] WallDirs = [0, 64, 128, 192];
    private static readonly int[] LampDirs = [0, 128, 192, 64];

    private readonly ArmyTable _armies;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutdoorLevelBuilder"/> class.
    /// </summary>
    /// <param name="armies">The armies monsters are drawn from.</param>
    public OutdoorLevelBuilder(ArmyTable armies)
    {
        _armies = armies;
    }

    /// <summary>
    /// Builds an outdoor level of the given style on the ground of the original it follows.
    /// </summary>
    public GeneratedLevel Build(Random rng, OutdoorStyle style, MapResourceBuilder original, TriggerArea? originalExit, IReadOnlyList<string> musicLines)
    {
        var size = original.Size;
        var lattice = new Lattice(LevelExtractor.LabStep.X, LevelExtractor.LabStep.Y, size.Width / 2, size.Height / 2);
        var start = new PointF(Margin * 1.5f, size.Height - Margin * 1.5f);
        OutdoorSite site;
        LevelScript script;
        int tries = 0;
        do
        {
            site = new OutdoorSite(rng, lattice, size, new MapBuilder(_armies));
            script = new LevelScript { MusicLines = musicLines, DoorsOpen = false };
            if (style == OutdoorStyle.City)
            {
                BuildCity(site, script);
            }
            else
            {
                BuildCompound(site, script, style, start, original, originalExit);
                site.Map.Add(KnownObject.PlayerStart, start.X, start.Y, 0, 16);
            }
        }
        while (style == OutdoorStyle.Campaign && script.Exit is not null && Distance(new PointF(script.Exit.Value.Center.X, script.Exit.Value.Center.Y), start) < MinExitLine && ++tries < CompoundTries);

        site.LayGround(original, style);
        var map = site.Map.ToMap(new Vector2(0, Math.Max(0, size.Height - 600)));
        map.Size = size;
        return new GeneratedLevel
        {
            Level = 1,
            Map = map,
            Builder = site.Map,
            Shown = site.Ground.Where(cell => !site.Interiors.Contains(cell)).ToHashSet(),
            Hidden = site.Interiors,
            Lattice = lattice,
            Template = ReferenceLevelSet.GetCampaignName(1),
            Theme = LevelTheme.Outdoor,
            Script = script
        };
    }

    // Build a fenced compound with two buildings, entered through a torn piece of the fence, with the exit inside the smaller building
    private static void BuildCompound(OutdoorSite site, LevelScript script, OutdoorStyle style, PointF start, MapResourceBuilder original, TriggerArea? originalExit)
    {
        var rng = site.Rng;
        var center = site.Lattice.ToLattice(site.Size.Width * 0.55f, site.Size.Height * 0.45f);
        int width = 2 * rng.Next(8, 11);
        int depth = 2 * rng.Next(7, 10);
        var yard = new UnitRect((int)center.U - width / 2, (int)center.V - depth / 2, width, depth);
        site.AddFence(yard, start);

        var big = site.PlaceBuilding(yard, rng.Next(6, 9), rng.Next(5, 7), BuildingGap, []);
        var small = site.PlaceBuilding(yard, rng.Next(4, 6), rng.Next(4, 5), BuildingGap, big is null ? [] : [big.Value]);
        var exitBuilding = small ?? big;
        if (exitBuilding is null)
        {
            return;
        }

        var breach = site.AddBuilding(exitBuilding.Value, breakable: true);
        if (big is not null && small is not null)
        {
            site.AddBuilding(big.Value, breakable: false);
        }

        var outside = site.GetOutsideCell(breach);
        var inside = breach.Cells.First == outside ? breach.Cells.Second : breach.Cells.First;
        var (wall, _) = StructureBuilder.GetLabPosition(site.Lattice, breach);
        var originalBreach = original.Sprites.FirstOrDefault(sprite => sprite.Id == KnownObject.BreakableWall2);
        bool copiesBreach = style == OutdoorStyle.Campaign && originalBreach is not null && originalExit is not null;
        if (copiesBreach)
        {
            // Copy the original's explosives at the breakable wall and its exit point just behind the wall
            var origin = new PointF(originalBreach!.Position.X, originalBreach.Position.Y);
            foreach (var sprite in original.Sprites.Where(s => s.Id is KnownObject.Barrel or KnownObject.Cylinder && Distance(new PointF(s.Position.X, s.Position.Y), origin) < BreachReach))
            {
                site.Map.Add(sprite.Id, wall.X + sprite.Position.X - origin.X, wall.Y + sprite.Position.Y - origin.Y, sprite.Position.Z, (int)sprite.Dir, unique: false);
            }

            var exit = originalExit!.Value;
            script.Exit = new TriggerArea(new Point((int)(wall.X + exit.Center.X - origin.X), (int)(wall.Y + exit.Center.Y - origin.Y)), exit.Radius);
        }
        else
        {
            (float ex, float ey) = site.Lattice.GetCenter(inside);
            script.Exit = new TriggerArea(new Point((int)ex, (int)ey), CompoundExitRadius);
        }

        site.Road = (start, site.Gate ?? start);
        site.AddYardScenes(yard, original, copiesBreach ? new PointF(originalBreach!.Position.X, originalBreach.Position.Y) : null);
        site.AddLamps();
        if (style == OutdoorStyle.Campaign)
        {
            site.AddMoneyRow(big ?? exitBuilding.Value, 4);
            return;
        }

        (float bx, float by) = site.Lattice.GetCenter(outside);
        site.Map.Add(KnownObject.DynamiteSite, bx, by, 0, 0);
        var charge = site.FindOpenSpot(point => Distance(point, new PointF(bx, by)) is >= ChargeLow and <= ChargeHigh && !yard.Contains(site.Lattice.GetCell(point.X, point.Y)));
        if (charge is not null)
        {
            site.Map.Add(KnownObject.Dynamite, charge.Value.X, charge.Value.Y, 0, 0);
        }

        site.Map.Add(KnownObject.Shotgun, start.X + 120, start.Y - 60, 0, 0);
        var middle = site.FindOpenSpot(point => yard.Contains(site.Lattice.GetCell(point.X, point.Y))) ?? site.Lattice.ToWorld(yard.U + yard.W / 2f, yard.V + yard.H / 2f);
        site.AddCluster(middle, [KnownObject.Monster1, KnownObject.YellowMonster1], 55, MonsterSpacing);
        var gate = site.Gate ?? middle;
        for (int spawner = 0; spawner < 2; spawner++)
        {
            var spot = site.FindOpenSpot(point => Distance(point, gate) is >= 900 and <= 1600);
            if (spot is not null)
            {
                var program = SpawnerProgram.Create(Enumerable.Repeat(KnownObject.Monster1, 12), (gate.X, gate.Y, SpawnerTriggerRadius), [gate]);
                site.Map.Add(KnownObject.Spawner, spot.Value.X, spot.Value.Y, 0, 0, program.ToPayload(), 1, unique: false);
            }
        }
    }

    // Build a grid of blocks with streets between them, put the player start in a corner, and put spawners along the streets and bats far off
    private static void BuildCity(OutdoorSite site, LevelScript script)
    {
        var rng = site.Rng;
        var area = site.GetGroundBounds();
        for (int u = area.U + 4; u + 8 < area.U + area.W; u += rng.Next(11, 14))
        {
            for (int v = area.V + 4; v + 8 < area.V + area.H; v += rng.Next(10, 13))
            {
                var block = new UnitRect(u, v, rng.Next(6, 9), rng.Next(5, 8));
                if (site.IsOnGround(block, 2))
                {
                    site.AddBuilding(block, breakable: false);
                }
            }
        }

        var start = new PointF(Margin * 2.5f, Margin * 2);
        site.Map.Add(KnownObject.PlayerStartFemaleLegs, start.X, start.Y, 0, 130);
        foreach (var item in new[] { KnownObject.Shotgun, KnownObject.Shotgun, KnownObject.GrenadeLauncher, KnownObject.LifeImplant, KnownObject.StrengthImplant })
        {
            var spot = site.FindOpenSpot(_ => true);
            if (spot is not null)
            {
                site.Map.Add(item, spot.Value.X, spot.Value.Y, 0, 0);
            }
        }

        for (int spawner = 0; spawner < 16; spawner++)
        {
            var spot = site.FindOpenSpot(point => Distance(point, start) >= 1200);
            if (spot is null)
            {
                continue;
            }

            var trigger = new PointF(spot.Value.X + rng.Next(-500, 501), spot.Value.Y + rng.Next(-400, 401));
            var program = SpawnerProgram.Create(Enumerable.Repeat(rng.Next(2) == 0 ? KnownObject.Monster1 : KnownObject.YellowMonster1, rng.Next(6, 14)),
                (trigger.X, trigger.Y, SpawnerTriggerRadius), [trigger]);
            site.Map.Add(KnownObject.Spawner, spot.Value.X, spot.Value.Y, 0, 0, program.ToPayload(), 1, unique: false);
        }

        var corner = new PointF(site.Size.Width - Margin * 4, site.Size.Height - Margin * 4);
        site.AddCluster(corner, [KnownObject.Bat], 42, BatSpacing);
        script.Exit = null;
    }

    private static float Distance(PointF a, PointF b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static float DistanceToSegment(PointF p, PointF a, PointF b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float length = dx * dx + dy * dy;
        float t = length == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length, 0, 1);
        return Distance(p, new PointF(a.X + t * dx, a.Y + t * dy));
    }

    // Step from one roof row to another two cells at a time, ending on the far row
    private static IEnumerable<float> GetRoofRows(float from, float to, float step)
    {
        for (float row = from; ; row += step)
        {
            if (step < 0 ? row <= to : row >= to)
            {
                yield return to;
                yield break;
            }

            yield return row;
        }
    }

    private sealed class OutdoorSite
    {
        private readonly HashSet<Cell> _taken = [];
        private readonly List<UnitRect> _buildings = [];
        private readonly List<(PointF Position, int Dir)> _posts = [];
        private UnitRect? _yard;

        public OutdoorSite(Random rng, Lattice lattice, SizeF size, MapBuilder map)
        {
            Rng = rng;
            Lattice = lattice;
            Size = size;
            Map = map;
            var corners = new[] { lattice.ToLattice(0, 0), lattice.ToLattice(size.Width, 0), lattice.ToLattice(0, size.Height), lattice.ToLattice(size.Width, size.Height) };
            for (int u = (int)MathF.Floor(corners.Min(c => c.U)); u <= (int)MathF.Ceiling(corners.Max(c => c.U)); u++)
            {
                for (int v = (int)MathF.Floor(corners.Min(c => c.V)); v <= (int)MathF.Ceiling(corners.Max(c => c.V)); v++)
                {
                    (float x, float y) = lattice.ToWorld(u + 0.5f, v + 0.5f);
                    if (x >= Margin && x <= size.Width - Margin && y >= Margin && y <= size.Height - Margin)
                    {
                        Ground.Add(new Cell(u, v));
                    }
                }
            }
        }

        public Random Rng { get; }

        public Lattice Lattice { get; }

        public SizeF Size { get; }

        public MapBuilder Map { get; }

        public HashSet<Cell> Ground { get; } = [];

        public HashSet<Cell> Interiors { get; } = [];

        public PointF? Gate { get; private set; }

        public (PointF Start, PointF Gate)? Road { get; set; }

        public UnitRect GetGroundBounds()
        {
            int u0 = Ground.Min(c => c.U), v0 = Ground.Min(c => c.V);
            return new UnitRect(u0, v0, Ground.Max(c => c.U) - u0 + 1, Ground.Max(c => c.V) - v0 + 1);
        }

        public bool IsOnGround(UnitRect rect, int margin)
        {
            for (int u = rect.U - margin; u < rect.U + rect.W + margin; u++)
            {
                for (int v = rect.V - margin; v < rect.V + rect.H + margin; v++)
                {
                    if (!Ground.Contains(new Cell(u, v)) || _taken.Contains(new Cell(u, v)))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        // Ring a cell rectangle with chain-link pieces two edges long, with a shut gate on the \ side facing the start and a torn piece at
        // the far end of the upper / side, and keep the posts between pieces for lamps
        public void AddFence(UnitRect yard, PointF start)
        {
            _yard = yard;
            var sides = new List<(PointF Center, bool Slash, int Index)>();
            for (int u = yard.U; u < yard.U + yard.W; u += FenceCells)
            {
                sides.Add((Lattice.ToWorld(u + 1, yard.V), true, 0));
                sides.Add((Lattice.ToWorld(u + 1, yard.V + yard.H), true, 1));
            }

            for (int v = yard.V; v < yard.V + yard.H; v += FenceCells)
            {
                sides.Add((Lattice.ToWorld(yard.U, v + 1), false, 2));
                sides.Add((Lattice.ToWorld(yard.U + yard.W, v + 1), false, 3));
            }

            var near = sides.Where(piece => !piece.Slash).MinBy(piece => Distance(piece.Center, start));
            var gate = sides.Where(piece => piece.Index == near.Index).OrderBy(piece => Distance(piece.Center, start)).Take(2).ToArray();
            var torn = sides.Where(piece => piece.Index == 0).MaxBy(piece => Distance(piece.Center, start));
            Gate = new PointF((gate[0].Center.X + gate[^1].Center.X) / 2, (gate[0].Center.Y + gate[^1].Center.Y) / 2);
            int leaf = 0;
            var openings = new List<PointF>();
            foreach (var piece in sides)
            {
                int dir = piece.Slash ? WallDirs[1 + 2 * Rng.Next(2)] : WallDirs[2 * Rng.Next(2)];
                if (gate.Contains(piece))
                {
                    Map.Add(KnownObject.LatticeFence, piece.Center.X, piece.Center.Y, 0, leaf++ == 0 ? 0 : 128);
                    openings.Add(piece.Center);
                }
                else if (piece == torn)
                {
                    Map.Add(KnownObject.TornLattice, piece.Center.X, piece.Center.Y, 0, 0);
                    openings.Add(piece.Center);
                }
                else
                {
                    Map.Add(KnownObject.LatticeWall, piece.Center.X, piece.Center.Y, 0, dir);
                }
            }

            // Stand lamps on the posts between pieces, away from the corners and the openings, facing into the yard
            var postsBySide = new List<(PointF Position, int Index)>();
            for (int u = yard.U + FenceCells; u < yard.U + yard.W; u += FenceCells)
            {
                postsBySide.Add((Lattice.ToWorld(u, yard.V), 0));
                postsBySide.Add((Lattice.ToWorld(u, yard.V + yard.H), 1));
            }

            for (int v = yard.V + FenceCells; v < yard.V + yard.H; v += FenceCells)
            {
                postsBySide.Add((Lattice.ToWorld(yard.U, v), 2));
                postsBySide.Add((Lattice.ToWorld(yard.U + yard.W, v), 3));
            }

            float pieceLength = Lattice.CellStep * FenceCells;
            foreach (var post in postsBySide.Where(post => openings.All(opening => Distance(opening, post.Position) > pieceLength)))
            {
                _posts.Add((post.Position, LampDirs[post.Index]));
            }

            // Keep the ground clear on both sides of the gate and the torn piece
            foreach (var opening in openings)
            {
                (float ou, float ov) = Lattice.ToLattice(opening.X, opening.Y);
                for (int u = (int)MathF.Floor(ou) - OpeningClearance; u <= (int)MathF.Floor(ou) + OpeningClearance; u++)
                {
                    for (int v = (int)MathF.Floor(ov) - OpeningClearance; v <= (int)MathF.Floor(ov) + OpeningClearance; v++)
                    {
                        _taken.Add(new Cell(u, v));
                    }
                }
            }

            for (int u = yard.U - 1; u <= yard.U + yard.W; u++)
            {
                _taken.Add(new Cell(u, yard.V - 1));
                _taken.Add(new Cell(u, yard.V + yard.H));
            }

            for (int v = yard.V - 1; v <= yard.V + yard.H; v++)
            {
                _taken.Add(new Cell(yard.U - 1, v));
                _taken.Add(new Cell(yard.U + yard.W, v));
            }
        }

        public UnitRect? PlaceBuilding(UnitRect yard, int width, int depth, int gap, IReadOnlyList<UnitRect> others)
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                int u = Rng.Next(yard.U + gap, Math.Max(yard.U + gap + 1, yard.U + yard.W - width - gap));
                int v = Rng.Next(yard.V + gap, Math.Max(yard.V + gap + 1, yard.V + yard.H - depth - gap));
                var rect = new UnitRect(u, v, width, depth);
                if (rect.U + rect.W <= yard.U + yard.W - gap && rect.V + rect.H <= yard.V + yard.H - gap
                    && others.All(other => !Overlaps(Inflate(other, gap), rect)))
                {
                    return rect;
                }
            }

            return null;
        }

        // Wall a block in, roof it, and shut it with a door on the lower u side, returning the edge left breakable on the upper v side
        // when asked for one
        public EdgeKey AddBuilding(UnitRect block, bool breakable)
        {
            var edges = new List<EdgeKey>();
            for (int u = block.U; u < block.U + block.W; u++)
            {
                edges.Add(new EdgeKey('v', u, block.V));
                edges.Add(new EdgeKey('v', u, block.V + block.H));
            }

            for (int v = block.V; v < block.V + block.H; v++)
            {
                edges.Add(new EdgeKey('u', block.U, v));
                edges.Add(new EdgeKey('u', block.U + block.W, v));
            }

            var doors = edges.Where(e => e.Axis == 'u' && e.A == block.U).ToArray();
            var door = doors[Rng.Next(doors.Length)];
            var breaches = edges.Where(e => e.Axis == 'v' && e.B == block.V + block.H && e.A > block.U && e.A < block.U + block.W - 1).ToArray();
            var breach = breakable && breaches.Length > 0 ? breaches[Rng.Next(breaches.Length)] : default;
            foreach (var edge in edges)
            {
                ((float x, float y), bool slash) = StructureBuilder.GetLabPosition(Lattice, edge);
                if (edge == door)
                {
                    Map.Add(KnownObject.SmallDoorWall2, x, y, 0, slash ? 0 : 128);
                    Map.Add(KnownObject.SmallDoorFrame, x, y, 0, slash ? 0 : 128);
                }
                else if (breakable && edge == breach)
                {
                    Map.Add(KnownObject.BreakableWall2, x, y, 0, slash ? 0 : 128);
                }
                else
                {
                    Map.Add(slash ? KnownObject.LabWall2Slash : KnownObject.LabWall2Back, x, y, 0, Rng.Next(256));
                }
            }

            // Put the beams where the wall lines meet
            foreach ((int a, int b) in new[] { (block.U, block.V), (block.U + block.W, block.V), (block.U, block.V + block.H), (block.U + block.W, block.V + block.H) })
            {
                (float x, float y) = Lattice.ToWorld(a + 0.25f, b + 0.25f);
                Map.Add(KnownObject.LabBeam2, x, y, 0, 0);
            }

            // Tile the roof two cells at a time from the back corner, a cell in from the walls
            foreach (float u in GetRoofRows(block.U + block.W - 1.25f, block.U + 0.75f, -2))
            {
                foreach (float v in GetRoofRows(block.V + 1.25f, block.V + block.H - 0.75f, 2))
                {
                    (float x, float y) = Lattice.ToWorld(u, v);
                    Map.Add(KnownObject.MilitaryFloor, x, y, RoofHeight, Rng.Next(256), unique: false);
                }
            }

            _buildings.Add(block);
            for (int u = block.U; u < block.U + block.W; u++)
            {
                for (int v = block.V; v < block.V + block.H; v++)
                {
                    _taken.Add(new Cell(u, v));
                    Interiors.Add(new Cell(u, v));
                }
            }

            _taken.Add(door.Cells.First);
            _taken.Add(door.Cells.Second);
            if (breakable)
            {
                _taken.Add(breach.Cells.First);
                _taken.Add(breach.Cells.Second);
            }

            return breach;
        }

        public Cell GetOutsideCell(EdgeKey edge) => Interiors.Contains(edge.Cells.First) ? edge.Cells.Second : edge.Cells.First;

        // Stamp the original's storage clusters in the yard, each against a building wall or the fence
        public void AddYardScenes(UnitRect yard, MapResourceBuilder original, PointF? originalBreach)
        {
            var props = original.Sprites
                .Where(s => YardProps.Contains(s.Id) && (originalBreach is null || Distance(new PointF(s.Position.X, s.Position.Y), originalBreach.Value) >= BreachReach))
                .ToList();
            var clusters = new List<List<MergedMapSprite>>();
            while (props.Count > 0)
            {
                var cluster = new List<MergedMapSprite> { props[0] };
                props.RemoveAt(0);
                for (int index = 0; index < cluster.Count; index++)
                {
                    var member = cluster[index];
                    var near = props.Where(p => Distance(new PointF(p.Position.X, p.Position.Y), new PointF(member.Position.X, member.Position.Y)) < ClusterReach).ToArray();
                    cluster.AddRange(near);
                    props.RemoveAll(near.Contains);
                }

                clusters.Add(cluster);
            }

            var yardCells = Ground.Where(c => yard.Contains(c) && !_taken.Contains(c)).ToArray();
            foreach (var cluster in clusters.OrderBy(_ => Rng.Next()))
            {
                float cx = cluster.Average(s => s.Position.X), cy = cluster.Average(s => s.Position.Y);
                for (int attempt = 0; attempt < 300 && yardCells.Length > 0; attempt++)
                {
                    var anchor = Lattice.GetCenter(yardCells[Rng.Next(yardCells.Length)]);
                    var spots = cluster.Select(s => (Sprite: s, X: anchor.X + s.Position.X - cx, Y: anchor.Y + s.Position.Y - cy)).ToArray();
                    var cells = spots.Select(spot => Lattice.GetCell(spot.X, spot.Y)).ToArray();
                    if (cells.Any(c => !yard.Contains(c) || _taken.Contains(c)) || !spots.Any(spot => IsAgainstWall(yard, spot.X, spot.Y)))
                    {
                        continue;
                    }

                    foreach (var spot in spots)
                    {
                        Map.Add(spot.Sprite.Id, spot.X, spot.Y, spot.Sprite.Position.Z, (int)spot.Sprite.Dir, spot.Sprite.Id == KnownObject.Crate ? KnownObject.CreateEmptyCratePayload() : default, unique: false);
                    }

                    _taken.UnionWith(cells);
                    break;
                }
            }
        }

        public void AddLamps()
        {
            int offset = Rng.Next(LampSpacing);
            for (int index = offset; index < _posts.Count; index += LampSpacing)
            {
                Map.Add(KnownObject.StreetLamp, _posts[index].Position.X, _posts[index].Position.Y, 0, _posts[index].Dir);
            }
        }

        public void AddMoneyRow(UnitRect building, int count)
        {
            (float x, float y) = Lattice.ToWorld(building.U - 0.5f, building.V + 1);
            for (int index = 0; index < count; index++)
            {
                Map.Add(KnownObject.Money, x + 40 * index, y + 28 * index, 0, 0);
            }
        }

        // Stand the monsters on the grid spots nearest the center that lie on open ground
        public void AddCluster(PointF center, IReadOnlyList<KnownObject> monsters, int count, float spacing)
        {
            int side = (int)MathF.Ceiling(MathF.Sqrt(count * ClusterSlack));
            var spots = Enumerable.Range(0, side * side)
                .Select(index =>
                {
                    (int row, int column) = Math.DivRem(index, side);
                    return new PointF(center.X + (column - side / 2f) * spacing, center.Y + (row - side / 2f) * spacing * 0.7f);
                })
                .Where(spot => !Interiors.Contains(Lattice.GetCell(spot.X, spot.Y)) && Ground.Contains(Lattice.GetCell(spot.X, spot.Y)))
                .OrderBy(spot => MathF.Abs(spot.X - center.X) + MathF.Abs(spot.Y - center.Y) / 0.7f)
                .Take(count);
            foreach (var spot in spots)
            {
                Map.Add(monsters[Rng.Next(monsters.Count)], spot.X, spot.Y, 0, Rng.Next(256), MapBuilder.MonsterPayload, 1, unique: false);
            }
        }

        public PointF? FindOpenSpot(Func<PointF, bool> isAllowed)
        {
            var cells = Ground.Where(c => !_taken.Contains(c)).ToArray();
            for (int attempt = 0; attempt < 400 && cells.Length > 0; attempt++)
            {
                var cell = cells[Rng.Next(cells.Length)];
                var point = Lattice.GetCenter(cell);
                if (isAllowed(point))
                {
                    _taken.Add(cell);
                    return point;
                }
            }

            return null;
        }

        // Lay the original's ground tiles, then grow grass clumps on the grass and tufts along its edge with the dirt
        public void LayGround(MapResourceBuilder original, OutdoorStyle style)
        {
            // Run each row of the original's ground tiles across the whole map, filling the gaps its buildings left
            var ground = style == OutdoorStyle.City ? KnownObject.Earth : KnownObject.Grass;
            foreach (var row in original.Sprites.Where(s => s.Id == ground && s.Position.Z == 0).GroupBy(s => MathF.Round(s.Position.Y)))
            {
                float[] xs = row.Select(s => s.Position.X).Order().ToArray();
                if (xs.Length < MinRowTiles)
                {
                    continue;
                }

                float step = xs.Zip(xs.Skip(1), (a, b) => b - a)
                    .Where(d => d > 0)
                    .GroupBy(MathF.Round)
                    .MaxBy(g => g.Count())!.Key;
                
                for (float x = xs[0] - MathF.Ceiling(xs[0] / step) * step; x <= Size.Width + step; x += step)
                {
                    Map.Add(ground, x, row.Key, 0, Rng.Next(256), unique: false);
                }
            }

            for (int row = 0; row * ClumpStepY < Size.Height; row++)
            {
                for (float x = row % 2 * ClumpStepX / 2; x < Size.Width; x += ClumpStepX)
                {
                    float px = x + (Rng.NextSingle() * 2 - 1) * ClumpJitter;
                    float py = row * ClumpStepY + (Rng.NextSingle() * 2 - 1) * ClumpJitter;
                    if (IsGrass(style, px, py))
                    {
                        Map.Add(KnownObject.VolumetricGrass, px, py, 0, Rng.Next(256), unique: false);
                    }
                }
            }

            for (float y = 0; y < Size.Height; y += TuftStep)
            {
                for (float x = 0; x < Size.Width; x += TuftStep)
                {
                    bool edge = !IsGrass(style, x + TuftReach, y) || !IsGrass(style, x - TuftReach, y) || !IsGrass(style, x, y + TuftReach) || !IsGrass(style, x, y - TuftReach);
                    if (IsGrass(style, x, y) && edge && Rng.NextSingle() < TuftChance)
                    {
                        Map.Add(KnownObject.FlatGrass, x + (Rng.NextSingle() * 2 - 1) * ClumpJitter, y + (Rng.NextSingle() * 2 - 1) * ClumpJitter, 1, Rng.Next(256), unique: false);
                    }
                }
            }
        }

        // Grow grass along the buildings' upper walls in every style, outside the campaign's yard and road, and in a band along the
        // compound's edges
        private bool IsGrass(OutdoorStyle style, float x, float y)
        {
            var point = new PointF(x, y);
            (float u, float v) = Lattice.ToLattice(x, y);
            if (_buildings.Any(b => u >= b.U && u <= b.U + b.W && v >= b.V && v <= b.V + b.H))
            {
                return false;
            }

            bool byBuilding = _buildings.Any(b => u >= b.U - WallGrass && u <= b.U + b.W + WallGrass && v >= b.V - WallGrass && v <= b.V + b.H + WallGrass
                && (u > b.U + b.W || v < b.V));
            bool inYard = _yard is not null && u >= _yard.Value.U && u <= _yard.Value.U + _yard.Value.W && v >= _yard.Value.V && v <= _yard.Value.V + _yard.Value.H;
            bool onRoad = Road.HasValue && IsOnRoad(point, Road.Value.Start, Road.Value.Gate);
            return style switch
            {
                OutdoorStyle.Campaign => byBuilding || !inYard && !onRoad,
                OutdoorStyle.Compound => byBuilding || x < BorderGrass || y < BorderGrass || x > Size.Width - BorderGrass || y > Size.Height - BorderGrass,
                _ => byBuilding
            };
        }

        // Run the road from the gate through the start and on toward the map's edge
        private static bool IsOnRoad(PointF point, PointF start, PointF gate)
        {
            float length = Math.Max(1, Distance(start, gate));
            var end = new PointF(start.X + (start.X - gate.X) / length * RoadOvershoot, start.Y + (start.Y - gate.Y) / length * RoadOvershoot);
            return DistanceToSegment(point, end, gate) < RoadHalfWidth;
        }

        private bool IsAgainstWall(UnitRect yard, float x, float y)
        {
            (float u, float v) = Lattice.ToLattice(x, y);
            bool byFence = u - yard.U < WallGrass || yard.U + yard.W - u < WallGrass || v - yard.V < WallGrass || yard.V + yard.H - v < WallGrass;
            bool byBuilding = _buildings.Any(b => u >= b.U - WallGrass && u <= b.U + b.W + WallGrass && v >= b.V - WallGrass && v <= b.V + b.H + WallGrass);
            return byFence || byBuilding;
        }

        private static UnitRect Inflate(UnitRect rect, int by) => new(rect.U - by, rect.V - by, rect.W + 2 * by, rect.H + 2 * by);

        private static bool Overlaps(UnitRect a, UnitRect b) => a.U < b.U + b.W && b.U < a.U + a.W && a.V < b.V + b.H && b.V < a.V + a.H;
    }
}
