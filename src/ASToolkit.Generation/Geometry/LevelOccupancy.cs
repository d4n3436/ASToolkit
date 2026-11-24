using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using ASToolkit.Formats;
using ASToolkit.Analysis.Levels;
using ASToolkit.Core;

namespace ASToolkit.Generation.Geometry;

/// <summary>
/// Represents the objects standing on a level's floor and the areas around its doorways kept clear of them.
/// </summary>
public sealed class LevelOccupancy
{
    private const float FloorHeight = 5;
    private const float OverlapShare = 0.3f;
    private const int DetourCells = 3;
    internal const float UnitGap = 12;
    private const float StandingHeight = 20;
    private const float SpawnerGap = 13;
    private const float SiteFrontClearance = 57;
    private const float ObstacleBucket = 128;

    private readonly ObjectTable _objects;
    private readonly Lattice _lattice;
    private readonly IReadOnlyDictionary<EdgeKey, EdgeKind> _edges;
    private readonly Cell _start;
    private readonly HashSet<Cell> _open;
    private readonly HashSet<Cell> _reserved = [];
    private readonly MapSegment[] _doorways;
    private readonly IReadOnlyDictionary<BlockingClass, float> _doorClearances;
    private readonly Func<KnownObject, KnownObject, bool, float> _spacingOf;
    private readonly WallClearanceTable _clearance;
    private readonly List<(PointF Center, float Reach)> _keepClear = [];
    private readonly List<PointF> _sites = [];
    private readonly List<StandingPiece> _standing = [];
    private readonly List<StandingPiece> _raised = [];
    private readonly Dictionary<Point, List<PointF>> _units = [];
    private readonly Dictionary<Point, List<CollisionBox>> _obstacles = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="LevelOccupancy"/> class.
    /// </summary>
    /// <param name="objects">The game objects.</param>
    /// <param name="lattice">The lattice of the floor cells.</param>
    /// <param name="floor">The floor cells that stay joined to the start.</param>
    /// <param name="edges">The edges between the floor cells.</param>
    /// <param name="start">The cell the player starts in.</param>
    /// <param name="doorways">The doorways to keep clear.</param>
    /// <param name="doorClearances">The distance from a doorway that floor pieces of each blocking class keep.</param>
    /// <param name="spacingOf">Returns how close two objects that block the player may stand at the same height, as the share of their summed sizes.</param>
    /// <param name="clearance">The wall clearances the original levels keep.</param>
    public LevelOccupancy(ObjectTable objects, Lattice lattice, IReadOnlySet<Cell> floor, IReadOnlyDictionary<EdgeKey, EdgeKind> edges, Cell start,
        IEnumerable<MapSegment> doorways, IReadOnlyDictionary<BlockingClass, float> doorClearances, Func<KnownObject, KnownObject, bool, float> spacingOf,
        WallClearanceTable clearance)
    {
        _spacingOf = spacingOf;
        _clearance = clearance;
        _objects = objects;
        _lattice = lattice;
        _edges = edges;
        _start = start;
        _open = floor.Flood([start], IsWall);
        _doorways = doorways.ToArray();
        _doorClearances = doorClearances;
    }

    /// <summary>
    /// Reserves the cells and keeps pieces that block the player off them.
    /// </summary>
    /// <param name="cells">The cells to reserve.</param>
    public void Reserve(IEnumerable<Cell> cells) => _reserved.UnionWith(cells);

    /// <summary>
    /// Keeps the collision boxes of the pieces that stand in the way at the given reach from a map point.
    /// </summary>
    /// <param name="center">The map point.</param>
    /// <param name="reach">The distance to keep clear, in map pixels.</param>
    public void KeepClear(PointF center, float reach) => _keepClear.Add((center, reach));

    /// <summary>
    /// Determines whether an object fits at a map point.
    /// </summary>
    /// <remarks>
    /// A unit has to keep apart from the other units and a pickup has to stand out from under the standing pieces. Any other object has to stay clear of the doorways, the floor pieces standing there and, outside the scene of a reserve, the reserved cells, and it can't split the floor when it is solid.
    /// </remarks>
    /// <param name="id">The object ID.</param>
    /// <param name="point">The map point.</param>
    /// <param name="z">The height.</param>
    /// <param name="inScene">Whether the object belongs to the reserve it stands in.</param>
    /// <returns><see langword="true"/> if the object fits; otherwise, <see langword="false"/>.</returns>
    public bool Fits(KnownObject id, PointF point, float z, bool inScene = false)
    {
        (float x, float y) = point;
        if (IsUnit(id))
        {
            return !IsNearUnit(x, y) && !IsInObstacle(id, x, y);
        }

        if ((z < FloorHeight || id.PickupCategory is not null) && !_clearance.KeepsDrop(id, _lattice, _edges, x, y))
        {
            return false;
        }

        if (id.PickupCategory is not null)
        {
            return !_standing.Any(standing => CollisionBox.Create(standing.Size, standing.X, standing.Y, CollisionBox.ExtentShare, 0).Contains(x, y));
        }

        var blocking = _objects.GetBlocking(id);
        if (!inScene && z < FloorHeight && (id.Family is not null || blocking != BlockingClass.WalkThrough) && IsInKeptSpace(id, x, y))
        {
            return false;
        }

        bool onFloor = z < FloorHeight && blocking != BlockingClass.WalkThrough;
        if (onFloor || id.IsWallMounted)
        {
            var clearanceClass = onFloor ? blocking : BlockingClass.Hittable;
            if (_doorClearances.TryGetValue(clearanceClass, out float clearance) && _doorways.Any(doorway => doorway.MeasureDistance(new PointF(x, y)) < clearance))
            {
                return false;
            }
        }

        if (blocking == BlockingClass.WalkThrough || !_objects.TryFind(id, out var obj))
        {
            return true;
        }

        bool raised = z >= FloorHeight;
        if (IsCrowded(id, obj, x, y, raised ? _raised : _standing, raised))
        {
            return false;
        }

        var box = CollisionBox.Create(obj, x, y, OverlapShare, 0);
        if (id.IsWallMounted && _standing.Any(standing => CollisionBox.Create(standing.Size, standing.X, standing.Y, OverlapShare, 0).Overlaps(box)))
        {
            return false;
        }

        var cells = GetCells(obj, x, y);
        return (inScene || !cells.Overlaps(_reserved)) && (blocking != BlockingClass.Solid || KeepsFloorJoined(cells));
    }

    /// <summary>
    /// Determines whether a map point keeps its distance from the solid or tall floor pieces standing around it.
    /// </summary>
    /// <param name="point">The map point.</param>
    /// <returns><see langword="true"/> if the point has room around it; otherwise, <see langword="false"/>.</returns>
    public bool HasRoomAround(PointF point) => !_standing.Any(standing =>
    {
        if (standing is { IsSolid: false, Size.Z: < StandingHeight })
        {
            return false;
        }

        float dx = Math.Max(0, Math.Abs(standing.X - point.X) - standing.Size.X / 2);
        float dy = Math.Max(0, Math.Abs(standing.Y - point.Y) - standing.Size.Y / 2);
        return dx * dx + dy * dy < SpawnerGap * SpawnerGap;
    });

    /// <summary>
    /// Adds an object standing at a map point.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <param name="point">The map point.</param>
    /// <param name="z">The height.</param>
    public void Add(KnownObject id, PointF point, float z)
    {
        (float x, float y) = point;
        if (id == KnownObject.DynamiteSite)
        {
            _sites.Add(point);
        }

        if (IsUnit(id))
        {
            var key = new Point((int)MathF.Floor(x / UnitGap), (int)MathF.Floor(y / UnitGap));
            ref var bucket = ref CollectionsMarshal.GetValueRefOrAddDefault(_units, key, out _);
            bucket ??= [];

            bucket.Add(new PointF(x, y));
            return;
        }

        if (!_objects.TryFind(id, out var obj))
        {
            return;
        }

        if (z < FloorHeight && StopsUnits(id, obj))
        {
            AddObstacle(CollisionBox.Create(obj, x, y, 0.5f, 0));
        }

        if (_objects.GetBlocking(id) == BlockingClass.WalkThrough)
        {
            return;
        }

        (z < FloorHeight ? _standing : _raised).Add(new StandingPiece(id, obj.Size, obj.IsSolid, x, y));

        if (obj.IsSolid)
        {
            _open.ExceptWith(GetCells(obj, x, y));
        }
    }

    private bool IsCrowded(KnownObject id, in GameObject obj, float x, float y, List<StandingPiece> others, bool raised)
    {
        foreach (var other in others)
        {
            float spacing = _spacingOf(id, other.Id, raised);
            if (Math.Abs(other.X - x) < spacing * (obj.Size.X + other.Size.X) && Math.Abs(other.Y - y) < spacing * (obj.Size.Y + other.Size.Y))
            {
                return true;
            }
        }

        return false;
    }

    private HashSet<Cell> GetCells(in GameObject obj, float x, float y) => CollisionBox.Create(obj, x, y, CollisionBox.ExtentShare, 0).GetSampledCells(_lattice);

    private bool KeepsFloorJoined(HashSet<Cell> cells)
    {
        var closing = cells.Where(_open.Contains).ToArray();
        if (closing.Length == 0)
        {
            return true;
        }

        if (closing.Contains(_start))
        {
            return false;
        }

        _open.ExceptWith(closing);
        bool joined = IsJoinedAround(closing) || _open.Flood([_start], IsWall).Count == _open.Count;
        _open.UnionWith(closing);
        return joined;
    }

    // Look for a way around the closed cells within a few cells of them
    private bool IsJoinedAround(Cell[] closing)
    {
        var sides = new HashSet<Cell>();
        foreach (var cell in closing)
        {
            foreach (var (neighbor, edge) in EdgeKey.GetNeighbors(cell))
            {
                if (_open.Contains(neighbor) && !IsWall(edge))
                {
                    sides.Add(neighbor);
                }
            }
        }

        if (sides.Count <= 1)
        {
            return true;
        }

        int minU = closing.Min(cell => cell.U) - DetourCells;
        int maxU = closing.Max(cell => cell.U) + DetourCells;
        int minV = closing.Min(cell => cell.V) - DetourCells;
        int maxV = closing.Max(cell => cell.V) + DetourCells;
        var window = new HashSet<Cell>();
        for (int u = minU; u <= maxU; u++)
        {
            for (int v = minV; v <= maxV; v++)
            {
                var cell = new Cell(u, v);
                if (_open.Contains(cell))
                {
                    window.Add(cell);
                }
            }
        }

        var reached = window.Flood([sides.First()], IsWall);
        return sides.All(reached.Contains);
    }

    /// <summary>
    /// Determines whether a map point lies in the floor space in front of a dynamite site, which no floor piece may take.
    /// </summary>
    /// <param name="site">The site's map point.</param>
    /// <param name="point">The map point.</param>
    /// <returns><see langword="true"/> if the point stands in front of the site; otherwise, <see langword="false"/>.</returns>
    public static bool IsInFrontOfSite(PointF site, PointF point) =>
        point.Y > site.Y && MathF.Sqrt((point.X - site.X) * (point.X - site.X) + (point.Y - site.Y) * (point.Y - site.Y)) < SiteFrontClearance;

    /// <summary>
    /// Determines whether a placed sprite is a floor piece the space in front of a site must stay clear of.
    /// </summary>
    /// <param name="id">The sprite's object ID.</param>
    /// <param name="z">The sprite's height.</param>
    /// <returns><see langword="true"/> if the sprite is a floor piece that the space in front of a site must stay clear of; otherwise, <see langword="false"/>.</returns>
    public static bool IsSiteBlocker(KnownObject id, float z) =>
        id.Family is not (null or PieceFamily.Site or PieceFamily.Generator) && id != KnownObject.TeleportPad && z < FloorHeight;

    private bool IsInKeptSpace(KnownObject id, float x, float y)
    {
        if (_sites.Any(site => IsInFrontOfSite(site, new PointF(x, y))))
            return true;

        if (!_objects.TryFind(id, out var obj))
            return false;

        var box = CollisionBox.Create(obj, x, y, CollisionBox.ExtentShare, 0);
        return _keepClear.Any(spot => GetBoxDistance(box, spot.Center) < spot.Reach);
    }

    private static float GetBoxDistance(CollisionBox box, PointF point)
    {
        var outside = Vector2.Max(Vector2.Zero, Vector2.Abs(new Vector2(box.X - point.X, box.Y - point.Y)) - new Vector2(box.HalfWidth, box.HalfHeight));
        return outside.Length();
    }

    private bool IsUnit(KnownObject id) => !id.IsDecor && _objects.TryFind(id, out var obj) && obj.SpriteType.HasFlag(SpriteType.Unit);

    private bool IsWall(EdgeKey edge) => _edges.TryGetValue(edge, out var kind) && kind.IsBlocking;

    private readonly record struct StandingPiece(KnownObject Id, Vector3 Size, bool IsSolid, float X, float Y);

    // Mirror the engine's movement map, where solid and outlined pieces raise the terrain and hittable ones stand as boxes
    private static bool StopsUnits(KnownObject id, in GameObject obj) =>
        (id == KnownObject.Crate || !(obj.SpriteType.HasFlag(SpriteType.Unit) || obj.SpriteType.HasFlag(SpriteType.Avia) || obj.SpriteType.HasFlag(SpriteType.Cannon)
                                      || obj.SpriteClass == SpriteClass.Man))
        && (obj.IsSolid || obj.Properties.HasFlag(ObjectProperties.BuildSizeToGridZ) || obj.IsHittable && obj.MoveMask != 0);

    private void AddObstacle(CollisionBox box)
    {
        for (int column = (int)MathF.Floor(box.MinX / ObstacleBucket); column <= (int)MathF.Floor(box.MaxX / ObstacleBucket); column++)
        {
            for (int row = (int)MathF.Floor(box.MinY / ObstacleBucket); row <= (int)MathF.Floor(box.MaxY / ObstacleBucket); row++)
            {
                var key = new Point(column, row);
                ref var bucket = ref CollectionsMarshal.GetValueRefOrAddDefault(_obstacles, key, out _);
                bucket ??= [];

                bucket.Add(box);
            }
        }
    }

    private bool IsInObstacle(KnownObject id, float x, float y)
    {
        _objects.TryFind(id, out var unit);
        var box = CollisionBox.Create(unit, x, y, 0.5f, 0);
        for (int column = (int)MathF.Floor(box.MinX / ObstacleBucket); column <= (int)MathF.Floor(box.MaxX / ObstacleBucket); column++)
        {
            for (int row = (int)MathF.Floor(box.MinY / ObstacleBucket); row <= (int)MathF.Floor(box.MaxY / ObstacleBucket); row++)
            {
                if (_obstacles.TryGetValue(new Point(column, row), out var bucket)
                    && bucket.Exists(obstacle => obstacle.Overlaps(box)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsNearUnit(float x, float y)
    {
        int column = (int)MathF.Floor(x / UnitGap), row = (int)MathF.Floor(y / UnitGap);
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (_units.TryGetValue(new Point(column + dx, row + dy), out var bucket)
                    && bucket.Exists(unit => MathF.Abs(unit.X - x) < UnitGap && MathF.Abs(unit.Y - y) < UnitGap))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
