using System.Drawing;
using System.Numerics;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Geometry;

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

    private readonly ObjectTable _objects;
    private readonly Lattice _lattice;
    private readonly IReadOnlyDictionary<EdgeKey, EdgeKind> _edges;
    private readonly Cell _start;
    private readonly HashSet<Cell> _open;
    private readonly HashSet<Cell> _reserved = [];
    private readonly MapSegment[] _doorways;
    private readonly IReadOnlyDictionary<BlockingClass, float> _doorClearances;
    private readonly Func<KnownObject, KnownObject, bool, float> _spacingOf;
    private readonly List<StandingPiece> _standing = [];
    private readonly List<StandingPiece> _raised = [];
    private readonly Dictionary<Point, List<PointF>> _units = [];

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
    public LevelOccupancy(ObjectTable objects, Lattice lattice, IReadOnlySet<Cell> floor, IReadOnlyDictionary<EdgeKey, EdgeKind> edges, Cell start,
        IEnumerable<MapSegment> doorways, IReadOnlyDictionary<BlockingClass, float> doorClearances, Func<KnownObject, KnownObject, bool, float> spacingOf)
    {
        _spacingOf = spacingOf;
        _objects = objects;
        _lattice = lattice;
        _edges = edges;
        _start = start;
        _open = floor.Flood([start], IsWall);
        _doorways = doorways.ToArray();
        _doorClearances = doorClearances;
    }

    /// <summary>
    /// Keeps cells free of pieces that block the player.
    /// </summary>
    public void Reserve(IEnumerable<Cell> cells) => _reserved.UnionWith(cells);

    /// <summary>
    /// Returns whether an object fits at a map point, apart from the other units for a unit, out from under the standing pieces for a pickup,
    /// and otherwise clear of the doorways, the floor pieces standing there and, outside a reserve's own scene, the reserved cells, leaving the floor joined when it is solid.
    /// </summary>
    public bool Fits(KnownObject id, PointF point, float z, bool inScene = false)
    {
        (float x, float y) = point;
        if (IsUnit(id))
        {
            return !IsNearUnit(x, y);
        }

        if (id.PickupCategory is not null)
        {
            return !_standing.Any(standing => CollisionBox.Create(standing.Size, standing.X, standing.Y, CollisionBox.ExtentShare, 0).Contains(x, y));
        }

        var blocking = _objects.GetBlocking(id);
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
    /// Returns whether a map point keeps its distance from the solid or tall floor pieces standing around it.
    /// </summary>
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
    /// Returns whether a group of objects placed together fits, each one on its own and beside the others, and the solid ones together leaving
    /// the floor joined.
    /// </summary>
    public bool FitsAll(IReadOnlyList<(KnownObject Id, float X, float Y, float Z)> pieces)
    {
        var cells = new HashSet<Cell>();
        var standing = new List<StandingPiece>();
        var raised = new List<StandingPiece>();
        foreach ((var id, float x, float y, float z) in pieces)
        {
            if (!Fits(id, new PointF(x, y), z))
            {
                return false;
            }

            if (IsUnit(id) || id.PickupCategory is not null || _objects.GetBlocking(id) == BlockingClass.WalkThrough || !_objects.TryFind(id, out var obj))
            {
                continue;
            }

            bool isRaised = z >= FloorHeight;
            var others = isRaised ? raised : standing;
            if (IsCrowded(id, obj, x, y, others, isRaised))
            {
                return false;
            }

            others.Add(new StandingPiece(id, obj.Size, obj.IsSolid, x, y));
            if (obj.IsSolid)
            {
                cells.UnionWith(GetCells(obj, x, y));
            }
        }

        return KeepsFloorJoined(cells);
    }

    /// <summary>
    /// Adds an object standing at a map point.
    /// </summary>
    public void Add(KnownObject id, PointF point, float z)
    {
        (float x, float y) = point;
        if (IsUnit(id))
        {
            var key = new Point((int)MathF.Floor(x / UnitGap), (int)MathF.Floor(y / UnitGap));
            if (!_units.TryGetValue(key, out var bucket))
            {
                _units[key] = bucket = [];
            }

            bucket.Add(new PointF(x, y));
            return;
        }

        if (_objects.GetBlocking(id) == BlockingClass.WalkThrough || !_objects.TryFind(id, out var obj))
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

    private bool IsUnit(KnownObject id) => !id.IsDecor && _objects.TryFind(id, out var obj) && obj.SpriteType.HasFlag(SpriteType.Unit);

    private bool IsWall(EdgeKey edge) => _edges.TryGetValue(edge, out var kind) && kind.BlocksWalking;

    private readonly record struct StandingPiece(KnownObject Id, Vector3 Size, bool IsSolid, float X, float Y);

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
