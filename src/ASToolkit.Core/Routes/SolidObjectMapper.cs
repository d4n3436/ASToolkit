using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Routes;

/// <summary>
/// Provides the floor cells that solid objects take away from the player.
/// </summary>
public static class SolidObjectMapper
{
    /// <summary>
    /// Returns the collision box of every solid object other than walls and floors. The box reaches 0.33 of the object's
    /// extent plus 14 pixels for the player's own size.
    /// </summary>
    public static CollisionBox[] GetCollisionBoxes(IEnumerable<MergedMapSprite> sprites, ObjectTable objects)
    {
        var boxes = new List<CollisionBox>();
        foreach (var sprite in sprites)
        {
            if (!objects.TryFind(sprite.Id, out var obj) || !obj.IsSolid || KnownObject.LabWalls.Contains(sprite.Id) || KnownObject.MilitaryWalls.Contains(sprite.Id) || KnownObject.Floors.Contains(sprite.Id))
            {
                continue;
            }

            float hx = 0.33f * obj.Size.X + 14;
            float hy = 0.33f * obj.Size.Y + 14;
            boxes.Add(new CollisionBox(sprite.Position.X - hx, sprite.Position.X + hx, sprite.Position.Y - hy, sprite.Position.Y + hy));
        }

        return boxes.ToArray();
    }

    /// <summary>
    /// Returns the cells at least half covered by the boxes, sampled 4 by 4 per cell.
    /// </summary>
    public static HashSet<Cell> FindBlockedCells(Lattice lattice, IReadOnlySet<Cell> cells, IReadOnlyList<CollisionBox> boxes)
    {
        var candidates = new Dictionary<Cell, List<int>>();
        for (int boxIndex = 0; boxIndex < boxes.Count; boxIndex++)
        {
            var b = boxes[boxIndex];
            var corners = new[] { lattice.ToLattice(b.MinX, b.MinY), lattice.ToLattice(b.MinX, b.MaxY), lattice.ToLattice(b.MaxX, b.MinY), lattice.ToLattice(b.MaxX, b.MaxY) };
            int u0 = (int)MathF.Floor(corners.Min(c => c.U));
            int u1 = (int)MathF.Floor(corners.Max(c => c.U));
            int v0 = (int)MathF.Floor(corners.Min(c => c.V));
            int v1 = (int)MathF.Floor(corners.Max(c => c.V));
            for (int u = u0; u <= u1; u++)
            {
                for (int v = v0; v <= v1; v++)
                {
                    var cell = new Cell(u, v);
                    if (!cells.Contains(cell))
                    {
                        continue;
                    }

                    if (!candidates.TryGetValue(cell, out var list))
                    {
                        list = [];
                        candidates[cell] = list;
                    }

                    list.Add(boxIndex);
                }
            }
        }

        var blocked = new HashSet<Cell>();
        foreach (var (cell, list) in candidates)
        {
            int covered = 0;
            for (int index = 0; index < 4; index++)
            {
                for (int innerIndex = 0; innerIndex < 4; innerIndex++)
                {
                    (float x, float y) = lattice.ToWorld(cell.U + (index + 0.5f) / 4, cell.V + (innerIndex + 0.5f) / 4);
                    if (IsCovered(boxes, list, x, y))
                    {
                        covered++;
                    }
                }
            }

            if (covered >= 8)
            {
                blocked.Add(cell);
            }
        }

        return blocked;
    }

    private static bool IsCovered(IReadOnlyList<CollisionBox> boxes, List<int> indexes, float x, float y)
    {
        foreach (int index in indexes)
        {
            if (boxes[index].Contains(x, y))
            {
                return true;
            }
        }

        return false;
    }
}
