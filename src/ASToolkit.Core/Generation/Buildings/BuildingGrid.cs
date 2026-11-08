using ASToolkit.Core.Levels;
using ASToolkit.Core.Rooms;

namespace ASToolkit.Core.Generation.Buildings;

/// <summary>
/// Provides the grid a building grammar places its rooms on, one grid unit a layout unit.
/// </summary>
internal sealed class BuildingGrid
{
    private readonly Dictionary<Cell, int> _owner = [];

    public List<GridRoom> Rooms { get; } = [];

    public GridRoom this[int id] => Rooms[id - 1];

    // Returns whether the rect is empty and keeps apart from every group but its own, the halls and the touch groups
    public bool IsFree(UnitRect rect, int group, int apart, IReadOnlyCollection<int> touch)
    {
        if (rect.W <= 0 || rect.H <= 0)
        {
            return false;
        }

        for (int u = rect.U - apart; u < rect.U + rect.W + apart; u++)
        {
            for (int v = rect.V - apart; v < rect.V + rect.H + apart; v++)
            {
                if (!_owner.TryGetValue(new Cell(u, v), out int owner))
                {
                    continue;
                }

                if (rect.Contains(new Cell(u, v)))
                {
                    return false;
                }

                int other = this[owner].Group;
                if (other != group && other != 0 && !touch.Contains(other))
                {
                    return false;
                }
            }
        }

        return apart == 0 || !LeavesSlit(rect);
    }

    public bool IsNeckFree(UnitRect rect, int group) => IsFree(rect, group, 0, []) && !LeavesSlit(rect);

    public bool LeavesSlit(UnitRect rect)
    {
        for (int u = rect.U; u < rect.U + rect.W; u++)
        {
            if (IsSlit(new Cell(u, rect.V - 1), new Cell(u, rect.V - 2)) || IsSlit(new Cell(u, rect.V + rect.H), new Cell(u, rect.V + rect.H + 1)))
            {
                return true;
            }
        }

        for (int v = rect.V; v < rect.V + rect.H; v++)
        {
            if (IsSlit(new Cell(rect.U - 1, v), new Cell(rect.U - 2, v)) || IsSlit(new Cell(rect.U + rect.W, v), new Cell(rect.U + rect.W + 1, v)))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsSlit(Cell gap, Cell beyond) =>
        !_owner.ContainsKey(gap) && _owner.TryGetValue(beyond, out int owner) && this[owner].Kind != GridKind.Court;

    public int? GetOwner(Cell cell) => _owner.TryGetValue(cell, out int owner) ? owner : null;

    public void Shrink(int id, UnitRect rect)
    {
        var room = this[id];
        foreach (var cell in room.Rect.Units.Where(cell => !rect.Contains(cell)))
        {
            _owner.Remove(cell);
        }

        room.Rect = rect;
    }

    public int Add(UnitRect rect, GridKind kind, int group, int phase, RoomType? type = null, bool alongU = true, bool isJoint = false, int? owner = null, int? host = null)
    {
        int id = Rooms.Count + 1;
        foreach (var cell in rect.Units)
        {
            _owner[cell] = id;
        }

        Rooms.Add(new GridRoom
        {
            Id = id, Rect = rect, Kind = kind, Group = group, Phase = phase, Type = type, AlongU = alongU, IsJoint = isJoint, Owner = owner, Host = host
        });

        return id;
    }
}
