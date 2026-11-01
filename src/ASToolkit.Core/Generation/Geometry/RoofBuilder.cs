using System.Drawing;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Geometry;

/// <summary>
/// Provides the earth that covers everything around the walkable floor.
/// </summary>
public sealed class RoofBuilder
{
    private const int StepX = 40;
    private const int StepY = 28;
    private const int Margin = 300;
    private const int Seam = 3;
    private const int WallOverlap = 10;

    private static readonly int[] Rounds = [0, 256, 64, 16, 4, 1];
    private static readonly (int Dx, int Dy)[] Sideways = Enumerable.Range(1, StepX / 2).SelectMany(step => new[] { (2 * step, 0), (-2 * step, 0) }).ToArray();

    private readonly bool _lab;
    private readonly int _roofZ;
    private readonly KnownObject _floorId;
    private readonly ColumnProfile _tile;
    private readonly ColumnProfile _inner;
    private readonly ColumnProfile _floor;
    private readonly ColumnProfile _cell;
    private readonly ColumnProfile _shownCell;
    private readonly ColumnProfile _slash;
    private readonly ColumnProfile _back;
    private readonly ColumnProfile _sunkenSlash;
    private readonly ColumnProfile _sunkenBack;

    /// <summary>
    /// Initializes a new instance of the <see cref="RoofBuilder"/> class.
    /// </summary>
    public RoofBuilder(LevelTheme theme, SpriteLibrary sprites)
    {
        _lab = theme == LevelTheme.Lab;
        _roofZ = _lab ? 90 : 95;
        _floorId = _lab ? KnownObject.BasementFloor : KnownObject.MilitaryFloor;
        var step = _lab ? Lattice.LabStep : Lattice.MilitaryStep;
        _tile = ColumnProfile.Read(sprites, KnownObject.Earth);
        _inner = _tile.Shrink(Seam);
        _floor = ColumnProfile.Read(sprites, _floorId);
        _cell = ColumnProfile.CreateDiamond((int)step.X, (int)step.Y, 0);
        _shownCell = ColumnProfile.CreateDiamond((int)step.X, (int)step.Y, 1);
        _slash = ColumnProfile.Read(sprites, _lab ? KnownObject.LabWallSlash : KnownObject.MilitaryWallSlash, _roofZ);
        _back = ColumnProfile.Read(sprites, _lab ? KnownObject.LabWallBack : KnownObject.MilitaryWallBack, _roofZ);
        _sunkenSlash = ColumnProfile.Read(sprites, KnownObject.LabWallSlash, _roofZ - StructureBuilder.SunkenWallZ);
        _sunkenBack = ColumnProfile.Read(sprites, KnownObject.LabWallBack, _roofZ - StructureBuilder.SunkenWallZ);
    }

    /// <summary>
    /// Adds the earth tiles to a map and returns the number of pixels left uncovered.
    /// </summary>
    public int Build(Random rng, FineLayout layout, IReadOnlySet<Cell> shown, IReadOnlySet<Cell> closets, MapBuilder map)
    {
        var lattice = layout.Lattice;
        var floors = map.Sprites.Where(sprite => sprite.Id == _floorId).Select(sprite => new PointF(sprite.Position.X, sprite.Position.Y)).ToArray();
        float minX = floors.Min(floor => floor.X) - Margin;
        float minY = floors.Min(floor => floor.Y) - Margin;
        var area = new Rectangle((int)MathF.Floor(minX), (int)MathF.Floor(minY - Margin),
            (int)MathF.Ceiling(floors.Max(floor => floor.X) + Margin - minX), (int)MathF.Ceiling(floors.Max(floor => floor.Y) + Margin - minY + Margin));
        var shapes = floors.Select(floor => CreateShape(_floor, floor, PixelKind.Floor))
            .Concat(layout.Cells.Keys.OrderBy(shown.Contains).Select(cell => shown.Contains(cell)
                ? CreateShape(_shownCell, lattice.GetCenter(cell), PixelKind.ShownFloor)
                : CreateShape(_cell, lattice.GetCenter(cell), closets.Contains(cell) ? PixelKind.ClosetFloor : PixelKind.Floor)))
            .Concat(layout.Edges.Keys
                .Where(edge => shown.Contains(edge.Cells.First) != shown.Contains(edge.Cells.Second))
                .Select(edge => CreateWall(lattice, edge, shown, false))
                .Concat(layout.Edges
                    .Where(pair => _lab && pair.Value == EdgeKind.Wall && !shown.Contains(pair.Key.Cells.First) && !shown.Contains(pair.Key.Cells.Second)
                        && (closets.Contains(pair.Key.Cells.First) || closets.Contains(pair.Key.Cells.Second)))
                    .Select(pair => CreateWall(lattice, pair.Key, shown, true)))
                .OrderBy(shape => shape.Top))
            .ToArray();

        (float ox, float oy) = (floors[0].X, floors[0].Y);
        var taken = floors.Select(floor => ((int)MathF.Round((floor.X - ox) / StepX), (int)MathF.Round((floor.Y - oy) / StepY))).ToHashSet();
        var classifier = new RoofClassifier(area, lattice, shown, WallOverlap);
        var visible = classifier.GetFloor(shapes);
        var spots = new List<(PointF Spot, int Row)>();
        var bare = new List<RoofShape>();
        for (int column = (int)MathF.Floor((area.Left - ox) / StepX) - 2; column <= (int)MathF.Ceiling((area.Right - ox) / StepX) + 2; column++)
        {
            for (int row = (int)MathF.Floor((area.Top - oy) / StepY) - 2; row <= (int)MathF.Ceiling((area.Bottom - oy) / StepY) + 2; row++)
            {
                var spot = new PointF(ox + StepX * column, oy + StepY * row);
                (int left, int top) = _inner.GetOrigin(spot.X, spot.Y);
                if (((column + row) & 1) != (_lab ? 0 : 1))
                {
                    continue;
                }

                spots.Add((spot, row));
                if (!taken.Contains((column, row)) && visible.Measure(_inner, left, top) > 0)
                {
                    bare.Add(CreateShape(_tile, spot, PixelKind.Bare));
                }
                else if (!taken.Contains((column, row)))
                {
                    map.Add(KnownObject.Earth, spot.X, spot.Y, 0, rng.Next(256), unique: false);
                }
            }
        }

        var (target, forbidden) = classifier.Classify(bare.Concat(shapes).ToArray());
        var order = spots.OrderBy(entry => entry.Row).ThenBy(entry => entry.Spot.X).Select(entry => entry.Spot).ToArray();
        int[] bounds = new int[order.Length];
        bounds.AsSpan().Fill(int.MaxValue);
        foreach (int least in Rounds)
        {
            for (int index = 0; index < order.Length; index++)
            {
                (int left, int top) = _tile.GetOrigin(order[index].X, order[index].Y);
                if (least > 0 && bounds[index] == int.MaxValue && !IsTargetNear(target, left, top))
                {
                    bounds[index] = 0;
                }

                while (least == 0 || bounds[index] >= least)
                {
                    (int dx, int dy, int covered) = FindBest(target, forbidden, left, top, least == 0 ? [(0, 0)] : GetSlides(lattice, order[index].X));
                    bounds[index] = least > 0 ? covered : bounds[index];
                    if (covered < Math.Max(1, least))
                    {
                        break;
                    }

                    target.Remove(_tile, left + dx, top + dy);
                    map.Add(KnownObject.Earth, order[index].X + dx, order[index].Y + dy + _roofZ, _roofZ, rng.Next(256), unique: false);
                }
            }
        }

        return target.Count;
    }

    // Slide vertically, also below the nearby cell corners where walls meet, and sideways
    private static (int Dx, int Dy)[] GetSlides(Lattice lattice, float x) =>
        Enumerable.Range((int)MathF.Ceiling((x - StepX - lattice.OriginX) / lattice.StepX), (int)(2 * StepX / lattice.StepX) + 1)
            .Select(corner => 2 * (int)MathF.Round((lattice.OriginX + lattice.StepX * corner - x) / 2))
            .Where(dx => Math.Abs(dx) <= StepX)
            .Prepend(0)
            .Distinct()
            .SelectMany(dx => Enumerable.Range(-StepY / 2, StepY + 1).Select(step => (dx, 2 * step)))
            .Concat(Sideways)
            .ToArray();

    // Keep a pixel away from forbidden rows to absorb the engine's rounding of sprite positions
    private (int Dx, int Dy, int Covered) FindBest(ColumnRuns target, ColumnRuns forbidden, int left, int top, (int Dx, int Dy)[] slides)
    {
        var best = (Dx: 0, Dy: 0, Covered: 0);
        foreach ((int dx, int dy) in slides)
        {
            int covered = target.Measure(_tile, left + dx, top + dy);
            if (covered > best.Covered && !forbidden.Touches(_tile, left + dx, top + dy, 1))
            {
                best = (dx, dy, covered);
            }
        }

        return best;
    }

    private bool IsTargetNear(ColumnRuns target, int left, int top) =>
        target.Measure(_tile, left - StepX, top - StepY, 2 * StepY) > 0 || target.Measure(_tile, left, top - StepY, 2 * StepY) > 0
        || target.Measure(_tile, left + StepX, top - StepY, 2 * StepY) > 0;

    // Stand each military piece, which spans two edges, on the vertex after its even edge, and lower sunken closet walls by their sink
    private RoofShape CreateWall(Lattice lattice, EdgeKey edge, IReadOnlySet<Cell> shown, bool sunken)
    {
        var (first, second) = edge.Cells;
        bool slash = edge.Axis == 'v';
        int along = (slash ? edge.A : edge.B) & 1;
        var position = _lab ? StructureBuilder.GetLabPosition(lattice, edge).Position
            : slash ? lattice.ToWorld(edge.A + 1 - along, edge.B) : lattice.ToWorld(edge.A, edge.B + 1 - along);
        bool frontShown = shown.Contains(lattice.GetCenter(first).Y > lattice.GetCenter(second).Y ? first : second);
        int start = (int)MathF.Round(lattice.ToWorld(edge.A, edge.B).X);
        var profile = sunken ? slash ? _sunkenSlash : _sunkenBack : slash ? _slash : _back;
        var wall = CreateShape(profile, new PointF(position.X, position.Y - (sunken ? StructureBuilder.SunkenWallZ : 0)), frontShown ? PixelKind.ShownFace : PixelKind.HiddenFace);
        return wall with { From = start - WallOverlap, To = start + (int)lattice.StepX + WallOverlap };
    }

    private static RoofShape CreateShape(ColumnProfile profile, PointF position, PixelKind kind)
    {
        (int left, int top) = profile.GetOrigin(position.X, position.Y);
        return new RoofShape(profile, left, top, kind);
    }
}
