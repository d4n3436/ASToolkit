using System.Drawing;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Geometry;

/// <summary>
/// Represents the objects standing on a level's floor and the areas around its doorways kept clear of them.
/// </summary>
public sealed class LevelOccupancy
{
    private const float FloorHeight = 5;
    private const float OverlapShare = 0.12f;
    private const int DetourCells = 3;
    private const float UnitGap = 12;
    private const float StandingHeight = 20;
    private const float LargeExtent = 40;
    private const float SmallGap = 16;
    private const float LargeGap = 30;

    private readonly ObjectTable _objects;
    private readonly Lattice _lattice;
    private readonly IReadOnlyDictionary<EdgeKey, EdgeKind> _edges;
    private readonly Cell _start;
    private readonly HashSet<Cell> _open;
    private readonly HashSet<Cell> _reserved = [];
    private readonly MapSegment[] _doorways;
    private readonly IReadOnlyDictionary<BlockingClass, float> _doorClearances;
    private readonly Func<KnownObject, KnownObject, bool, float> _spacingOf;
    private readonly List<(KnownObject Id, GameObject Object, float X, float Y)> _standing = [];
    private readonly List<(KnownObject Id, GameObject Object, float X, float Y)> _raised = [];
    private readonly List<PointF> _units = [];

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
    /// and otherwise clear of the doorways and the floor pieces standing there, leaving the floor joined when it is solid.
    /// </summary>
    public bool Fits(KnownObject id, float x, float y, float z)
    {
        if (IsUnit(id))
        {
            return !_units.Any(unit => MathF.Abs(unit.X - x) < UnitGap && MathF.Abs(unit.Y - y) < UnitGap);
        }

        if (id.PickupCategory is not null)
        {
            return !_standing.Any(standing => CollisionBox.Create(standing.Object, standing.X, standing.Y, CollisionBox.ExtentShare, 0).Contains(x, y));
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
        if (id.IsWallMounted && _standing.Any(standing => CollisionBox.Create(standing.Object, standing.X, standing.Y, OverlapShare, 0).Overlaps(box)))
        {
            return false;
        }

        var cells = GetCells(obj, x, y);
        return !cells.Overlaps(_reserved) && (blocking != BlockingClass.Solid || KeepsFloorJoined(cells));
    }

    /// <summary>
    /// Returns whether a map point keeps its distance from the solid or tall floor pieces standing around it, farther from large ones.
    /// </summary>
    public bool HasRoomAround(float x, float y) => !_standing.Any(standing =>
    {
        var obj = standing.Object;
        if (!obj.IsSolid && obj.Size.Z < StandingHeight)
        {
            return false;
        }

        float dx = Math.Max(0, Math.Abs(standing.X - x) - obj.Size.X / 2);
        float dy = Math.Max(0, Math.Abs(standing.Y - y) - obj.Size.Y / 2);
        float gap = Math.Max(obj.Size.X, obj.Size.Y) >= LargeExtent ? LargeGap : SmallGap;
        return dx * dx + dy * dy < gap * gap;
    });

    /// <summary>
    /// Returns whether a group of objects placed together fits, each one on its own and beside the others, and the solid ones together leaving
    /// the floor joined.
    /// </summary>
    public bool FitsAll(IReadOnlyList<(KnownObject Id, float X, float Y, float Z)> pieces)
    {
        var cells = new HashSet<Cell>();
        var standing = new List<(KnownObject Id, GameObject Object, float X, float Y)>();
        var raised = new List<(KnownObject Id, GameObject Object, float X, float Y)>();
        foreach ((var id, float x, float y, float z) in pieces)
        {
            if (!Fits(id, x, y, z))
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

            others.Add((id, obj, x, y));
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
    public void Add(KnownObject id, float x, float y, float z)
    {
        if (IsUnit(id))
        {
            _units.Add(new PointF(x, y));
            return;
        }

        if (_objects.GetBlocking(id) == BlockingClass.WalkThrough || !_objects.TryFind(id, out var obj))
        {
            return;
        }

        (z < FloorHeight ? _standing : _raised).Add((id, obj, x, y));

        if (obj.IsSolid)
        {
            _open.ExceptWith(GetCells(obj, x, y));
        }
    }

    private bool IsCrowded(KnownObject id, GameObject obj, float x, float y, List<(KnownObject Id, GameObject Object, float X, float Y)> others, bool raised)
    {
        foreach (var other in others)
        {
            float spacing = _spacingOf(id, other.Id, raised);
            if (Math.Abs(other.X - x) < spacing * (obj.Size.X + other.Object.Size.X) && Math.Abs(other.Y - y) < spacing * (obj.Size.Y + other.Object.Size.Y))
            {
                return true;
            }
        }

        return false;
    }

    // Sample the collision box at its corners, edge midpoints and center
    private HashSet<Cell> GetCells(GameObject obj, float x, float y)
    {
        var box = CollisionBox.Create(obj, x, y, CollisionBox.ExtentShare, 0);
        var cells = new HashSet<Cell>();
        foreach (float dx in new[] { -box.HalfWidth, 0, box.HalfWidth })
        {
            foreach (float dy in new[] { -box.HalfHeight, 0, box.HalfHeight })
            {
                cells.Add(_lattice.GetCell(x + dx, y + dy));
            }
        }

        return cells;
    }

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

    private bool IsWall(EdgeKey edge) => _edges.TryGetValue(edge, out EdgeKind kind) && kind.BlocksWalking;
}
