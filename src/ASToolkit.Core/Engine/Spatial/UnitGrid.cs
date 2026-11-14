using System.Numerics;
using System.Runtime.InteropServices;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal sealed class UnitGrid
{
    private const float BucketSize = 64;
    public const float MaxHalfSize = 65;
    private const float MaxPickReach = 260;

    private readonly Vector2 _origin;
    private readonly int _columns;
    private readonly int _rows;
    private readonly List<Unit>[] _buckets;

    public UnitGrid(Vector2 min, Vector2 max)
    {
        _origin = min;
        _columns = (int)((max.X - min.X) / BucketSize) + 1;
        _rows = (int)((max.Y - min.Y) / BucketSize) + 1;
        _buckets = new List<Unit>[_columns * _rows];
        for (int bucket = 0; bucket < _buckets.Length; bucket++)
        {
            _buckets[bucket] = [];
        }
    }

    public Vector2 MaxHalf { get; private set; }

    public void Add(Unit unit)
    {
        MaxHalf = Vector2.Max(MaxHalf, unit.Kind.Half);
        unit.Bucket = GetBucket(unit.Position.AsVector2());
        var bucket = _buckets[unit.Bucket];
        unit.Slot = bucket.Count;
        bucket.Add(unit);
    }

    public void Remove(Unit unit)
    {
        if (unit.Slot < 0)
            return;

        var bucket = _buckets[unit.Bucket];
        var last = bucket[^1];
        bucket[unit.Slot] = last;
        last.Slot = unit.Slot;
        bucket.RemoveAt(bucket.Count - 1);
        unit.Slot = -1;
    }

    public void Move(Unit unit)
    {
        MaxHalf = Vector2.Max(MaxHalf, unit.Kind.Half);

        if (unit.Slot < 0 || GetBucket(unit.Position.AsVector2()) == unit.Bucket)
        {
            return;
        }

        Remove(unit);
        Add(unit);
    }

    public Unit? FindOverlap(Unit mover, in CollisionVolume volume)
    {
        var center = volume.Position;
        var reach = volume.Half + MaxHalf;
        int firstColumn = GetColumn(center.X - reach.X), lastColumn = GetColumn(center.X + reach.X);
        int lastRow = GetRow(center.Y + reach.Y);
        for (int row = GetRow(center.Y - reach.Y); row <= lastRow; row++)
        {
            for (int column = firstColumn; column <= lastColumn; column++)
            {
                foreach (var other in CollectionsMarshal.AsSpan(_buckets[row * _columns + column]))
                {
                    if (other == mover || other.Animation >= SpriteAnimation.Death)
                        continue;

                    var kind = other.Kind;
                    var position = other.Position;
                    var half = volume.Half + kind.Half;
                    if ((volume.Mask & kind.MoveMask) != 0 && MathF.Abs(center.X - position.X) < half.X && MathF.Abs(center.Y - position.Y) < half.Y &&
                        center.Z <= position.Z + kind.Object.Size.Z && position.Z <= center.Z + volume.Height)
                        return other;
                }
            }
        }

        return null;
    }

    public UnitQuery GetNear(Vector2 min, Vector2 max) => new(_buckets, _columns, GetColumn(min.X), GetColumn(max.X), GetRow(min.Y), GetRow(max.Y));

    public void Collect(Vector2 min, Vector2 max, List<Unit> found)
    {
        found.Clear();
        foreach (var unit in GetNear(min, max))
        {
            found.Add(unit);
        }
    }

    [EngineRoutine(0x0040fc40)]
    public bool HasUnitAt(Vector2 point)
    {
        foreach (var unit in GetNear(point - new Vector2(MaxHalfSize), point + new Vector2(MaxHalfSize, MaxPickReach)))
        {
            if (unit.Kind.Object.SpriteType.HasFlag(SpriteType.Unit) && unit.CollisionVolume.Covers(point))
                return true;
        }

        return false;
    }

    private int GetBucket(Vector2 point) => GetRow(point.Y) * _columns + GetColumn(point.X);

    private int GetColumn(float x) => Math.Clamp((int)((x - _origin.X) / BucketSize), 0, _columns - 1);

    private int GetRow(float y) => Math.Clamp((int)((y - _origin.Y) / BucketSize), 0, _rows - 1);
}

internal struct UnitQuery
{
    private readonly List<Unit>[] _buckets;
    private readonly int _columns;
    private readonly int _firstColumn;
    private readonly int _lastColumn;
    private readonly int _lastRow;
    private int _column;
    private int _row;
    private int _index;

    public UnitQuery(List<Unit>[] buckets, int columns, int firstColumn, int lastColumn, int firstRow, int lastRow)
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

    public readonly Unit Current => _buckets[_row * _columns + _column][_index];

    public readonly UnitQuery GetEnumerator() => this;

    public bool MoveNext()
    {
        while (_row <= _lastRow)
        {
            if (++_index < _buckets[_row * _columns + _column].Count)
            {
                return true;
            }

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
