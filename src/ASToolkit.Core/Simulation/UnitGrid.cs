using System.Numerics;

namespace ASToolkit.Core.Simulation;

internal sealed class UnitGrid
{
    private const float BucketSize = 128;

    private readonly Vector2 _origin;
    private readonly int _columns;
    private readonly int _rows;
    private readonly List<CombatUnit>[] _buckets;

    public UnitGrid(Vector2 min, Vector2 max)
    {
        _origin = min;
        _columns = (int)((max.X - min.X) / BucketSize) + 1;
        _rows = (int)((max.Y - min.Y) / BucketSize) + 1;
        _buckets = new List<CombatUnit>[_columns * _rows];
        for (int bucket = 0; bucket < _buckets.Length; bucket++)
        {
            _buckets[bucket] = [];
        }
    }

    public void Add(CombatUnit unit)
    {
        unit.Bucket = GetBucket(unit.Position);
        unit.Slot = _buckets[unit.Bucket].Count;
        _buckets[unit.Bucket].Add(unit);
    }

    public void Remove(CombatUnit unit)
    {
        var bucket = _buckets[unit.Bucket];
        var last = bucket[^1];
        bucket[unit.Slot] = last;
        last.Slot = unit.Slot;
        bucket.RemoveAt(bucket.Count - 1);
    }

    public void Move(CombatUnit unit)
    {
        if (GetBucket(unit.Position) == unit.Bucket)
            return;

        Remove(unit);
        Add(unit);
    }

    // Enumerates the units of every bucket the box touches, without copying them
    public NearbyUnits GetNear(Vector2 min, Vector2 max) => new(_buckets, _columns, GetColumn(min.X), GetColumn(max.X), GetRow(min.Y), GetRow(max.Y));

    // Fills the list with the units near the box, for callers that change the grid while they go through them
    public void Collect(Vector2 min, Vector2 max, List<CombatUnit> found)
    {
        found.Clear();
        foreach (var unit in GetNear(min, max))
        {
            found.Add(unit);
        }
    }

    private int GetBucket(Vector2 point) => GetRow(point.Y) * _columns + GetColumn(point.X);

    private int GetColumn(float x) => Math.Clamp((int)((x - _origin.X) / BucketSize), 0, _columns - 1);

    private int GetRow(float y) => Math.Clamp((int)((y - _origin.Y) / BucketSize), 0, _rows - 1);
}

internal struct NearbyUnits
{
    private readonly List<CombatUnit>[] _buckets;
    private readonly int _columns;
    private readonly int _firstColumn;
    private readonly int _lastColumn;
    private readonly int _lastRow;
    private int _column;
    private int _row;
    private int _index;

    public NearbyUnits(List<CombatUnit>[] buckets, int columns, int firstColumn, int lastColumn, int firstRow, int lastRow)
    {
        _buckets = buckets;
        _columns = columns;
        _firstColumn = firstColumn;
        _lastColumn = lastColumn;
        _lastRow = lastRow;
        _column = firstColumn;
        _row = firstRow;
        _index = -1;
    }

    public readonly CombatUnit Current => _buckets[_row * _columns + _column][_index];

    public readonly NearbyUnits GetEnumerator() => this;

    public bool MoveNext()
    {
        while (_row <= _lastRow)
        {
            if (++_index < _buckets[_row * _columns + _column].Count)
                return true;

            _index = -1;
            if (++_column > _lastColumn)
            {
                _column = _firstColumn;
                _row++;
            }
        }

        return false;
    }
}
