using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal sealed class SpriteCollector
{
    private readonly Vector2 _scale;
    private readonly Vector2 _cell;
    private readonly int _columns;
    private readonly int _rows;
    private readonly List<GridEntry>[] _buckets;
    private readonly List<Sprite> _sprites = [];
    private int _firstColumn;
    private int _lastColumn;
    private int _lastRow;
    private int _column;
    private int _row;
    private int _index;

    // Counts columns on the vertical cell scale
    [EngineRoutine(0x0042d380)]
    public SpriteCollector(SizeF mapSize, Vector2 largestObject)
    {
        _cell = new Vector2(GetCellSize(largestObject.X), GetCellSize(largestObject.Y));
        LargestHalf = largestObject / 2;
        _scale = Vector2.One / _cell;
        int shift = 0;
        while (1 << shift < (mapSize.Width - 1) * _scale.Y + 1)
        {
            shift++;
        }

        _columns = 1 << shift;
        _rows = (int)((mapSize.Height - 1) * _scale.Y + 3);
        _buckets = new List<GridEntry>[_columns * _rows];
        for (int bucket = 0; bucket < _buckets.Length; bucket++)
        {
            _buckets[bucket] = [];
        }
    }

    public static bool IsHashed(SpriteDefinition definition) => definition.Object.Properties.HasFlag(ObjectProperties.Hash);

    private static float GetCellSize(float size)
    {
        int shift = 0;
        while (1 << shift < size * 0.5f)
        {
            shift++;
        }

        return 1 << shift;
    }

    public ReadOnlySpan<Sprite> Listed => CollectionsMarshal.AsSpan(_sprites);

    public Vector2 LargestHalf { get; }

    private const OldSpriteType ListedTypes = OldSpriteType.Unit | OldSpriteType.Avia;

    public static bool IsListed(SpriteDefinition definition) => (definition.Object.SpriteType.ToOldSpriteType() & ListedTypes) != 0;

    [EngineRoutine(0x0042d6a0)]
    public void InsertToHash(Sprite sprite)
    {
        if (IsHashed(sprite.Definition))
        {
            AddToBucket(sprite);
        }

        if (IsListed(sprite.Definition))
        {
            sprite.ListIndex = _sprites.Count;
            _sprites.Add(sprite);
        }
    }

    [EngineRoutine(0x0042d770)]
    public void DeleteFromHash(Sprite sprite)
    {
        if (sprite.Slot >= 0)
        {
            RemoveFromBucket(sprite);
        }

        if (sprite.ListIndex < 0)
            return;

        var last = _sprites[^1];
        _sprites[sprite.ListIndex] = last;
        last.ListIndex = sprite.ListIndex;
        _sprites.RemoveAt(_sprites.Count - 1);
        sprite.ListIndex = -1;
    }

    private void AddToBucket(Sprite sprite)
    {
        sprite.Bucket = GetBucket(sprite.Position.AsVector2());
        var bucket = _buckets[sprite.Bucket];
        sprite.Slot = bucket.Count;
        sprite.Collector = this;
        bucket.Add(new GridEntry(sprite));
    }

    private void RemoveFromBucket(Sprite sprite)
    {
        var bucket = _buckets[sprite.Bucket];
        var last = bucket[^1];
        bucket[sprite.Slot] = last;
        last.Sprite.Slot = sprite.Slot;
        bucket.RemoveAt(bucket.Count - 1);
        sprite.Slot = -1;
        sprite.Collector = null;
    }

    [EngineRoutine(0x0042d8c0)]
    public void ChangeCoor(Sprite sprite)
    {
        if (sprite.Slot < 0)
            return;

        if (GetBucket(sprite.Position.AsVector2()) == sprite.Bucket)
        {
            CollectionsMarshal.AsSpan(_buckets[sprite.Bucket])[sprite.Slot] = new GridEntry(sprite);
            return;
        }

        RemoveFromBucket(sprite);
        AddToBucket(sprite);
    }

    public Sprite? First(Vector2 min, Vector2 max)
    {
        FirstHashInBox(min, max);

        return NextHashInBox();
    }

    [EngineRoutine(0x0042dd60)]
    public void FirstHashInBox(Vector2 min, Vector2 max)
    {
        _firstColumn = GetColumn(min.X - _cell.X);
        _row = GetRow(min.Y - _cell.Y);
        _lastColumn = GetColumn(max.X + _cell.X);
        _lastRow = GetRow(max.Y + _cell.Y);
        _column = _firstColumn;
        _index = 0;
    }

    [EngineRoutine(0x0042deb0)]
    public Sprite? NextHashInBox()
    {
        for (; _row <= _lastRow; _row++, _column = _firstColumn, _index = 0)
        {
            for (; _column <= _lastColumn; _column++, _index = 0)
            {
                var bucket = _buckets[(_row * _columns) + _column];

                if (_index < bucket.Count)
                    return bucket[_index++].Sprite;
            }
        }

        return null;
    }

    [EngineRoutine(0x004433f0)]
    public Sprite? FindOverlap(Sprite? mover, in CollisionVolume volume)
    {
        var center = volume.Position;
        var reach = volume.Half + LargestHalf;
        First(center.AsVector2() - volume.Half, center.AsVector2() + volume.Half);
        int firstColumn = Math.Max(_firstColumn, GetColumn(center.X - reach.X));
        int lastColumn = Math.Min(_lastColumn, GetColumn(center.X + reach.X));
        int lastRow = Math.Min(_lastRow, GetRow(center.Y + reach.Y));
        
        for (int row = Math.Max(_row, GetRow(center.Y - reach.Y)); row <= lastRow; row++)
        {
            for (int column = firstColumn; column <= lastColumn; column++)
            {
                var bucket = CollectionsMarshal.AsSpan(_buckets[(row * _columns) + column]);
                for (int i = 0; i < bucket.Length; i++)
                {
                    ref readonly var entry = ref bucket[i];

                    if (entry.Sprite != mover && entry.Sprite.Animation < SpriteAnimation.Death && MathF.Abs(entry.Position.X - center.X) < entry.Half.X + volume.Half.X &&
                        MathF.Abs(entry.Position.Y - center.Y) < entry.Half.Y + volume.Half.Y &&
                        center.Z <= entry.Height + entry.Position.Z && entry.Position.Z <= center.Z + volume.Height && (volume.Mask & entry.Mask) != 0)
                    {
                        _row = row;
                        _column = column;
                        _index = i + 1;

                        return entry.Sprite;
                    }
                }
            }
        }

        _row = _lastRow + 1;

        return null;
    }

    
    public HashQuery GetNear(Vector2 min, Vector2 max) => new(_buckets, _columns, GetColumn(min.X), GetColumn(max.X), GetRow(min.Y), GetRow(max.Y));

    private int GetBucket(Vector2 point) => (GetRow(point.Y) * _columns) + GetColumn(point.X);

    private int GetColumn(float x) => GetCell(x * _scale.X, _columns);

    private int GetRow(float y) => GetCell(y * _scale.Y, _rows);

    private static int GetCell(float scaled, int count) => scaled < 0 ? 0 : scaled < count ? (int)scaled : count - 1;
}

internal struct HashQuery
{
    private readonly List<GridEntry>[] _buckets;
    private readonly int _columns;
    private readonly int _firstColumn;
    private readonly int _lastColumn;
    private readonly int _lastRow;
    private int _column;
    private int _row;
    private int _index;

    public HashQuery(List<GridEntry>[] buckets, int columns, int firstColumn, int lastColumn, int firstRow, int lastRow)
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

    public readonly Sprite Current => _buckets[_row * _columns + _column][_index].Sprite;

    public readonly HashQuery GetEnumerator() => this;

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

internal readonly struct GridEntry
{
    public GridEntry(Sprite sprite)
    {
        Sprite = sprite;
        Position = sprite.Position;
        Half = sprite.Definition.Half;
        Height = sprite.Definition.Object.Size.Z;
        Mask = sprite.Definition.MoveMask;
    }

    public Sprite Sprite { get; }

    public Vector3 Position { get; }

    public Vector2 Half { get; }

    public float Height { get; }

    public CollisionLayers Mask { get; }
}
