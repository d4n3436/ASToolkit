using System.Drawing;
using System.Numerics;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Provides the spatial hash of the sprites of a map that collide, and the list of its units and flying units.
/// </summary>
public sealed class SpriteCollector
{
    private const OldSpriteType ListedTypes = OldSpriteType.Unit | OldSpriteType.Avia;

    private readonly Vector2 _scale;
    private readonly Vector2 _cell;
    private readonly int _columns;
    private readonly int _rows;
    private readonly SpriteList[] _buckets;
    private readonly SpriteList _listed = new();
    private int _firstColumn;
    private int _lastColumn;
    private int _lastRow;
    private int _column;
    private int _row;
    private int _index;

    [EngineRoutine(0x0042d380)]
    internal SpriteCollector(SizeF mapSize, ReadOnlySpan<SpriteDefinition?> definitions)
    {
        var largestObject = Vector2.Zero;
        foreach (var definition in definitions)
        {
            if (definition is not null && IsHashed(definition))
            {
                largestObject = Vector2.Max(largestObject, definition.Object.Size.AsVector2());
            }
        }

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
        _buckets = new SpriteList[_columns * _rows];
        for (int bucket = 0; bucket < _buckets.Length; bucket++)
        {
            _buckets[bucket] = new SpriteList();
        }
    }

    /// <summary>
    /// Gets the listed sprites, the units and flying units of the map.
    /// </summary>
    public ReadOnlySpan<Sprite> Listed => _listed.AsSpan();

    /// <summary>
    /// Gets half the width and depth of the largest object.
    /// </summary>
    public Vector2 LargestHalf { get; }

    /// <summary>
    /// Gets the sprites in the buckets a box covers.
    /// </summary>
    /// <param name="min">The top left corner of the box.</param>
    /// <param name="max">The bottom right corner of the box.</param>
    /// <returns>The query over the sprites.</returns>
    public HashQuery GetNear(Vector2 min, Vector2 max) => new(_buckets, _columns, GetColumn(min.X), GetColumn(max.X), GetRow(min.Y), GetRow(max.Y));

    internal static bool IsHashed(SpriteDefinition definition) => definition.Object.Properties.HasFlag(ObjectProperties.Hash);

    internal static bool IsListed(SpriteDefinition definition) => (definition.Object.SpriteType.ToOldSpriteType() & ListedTypes) != 0;

    [EngineRoutine(0x0042d6a0)]
    internal void InsertToHash(Sprite sprite)
    {
        if (IsHashed(sprite.Definition))
        {
            _buckets[GetBucket(sprite.Position.AsVector2())].Add(sprite);
        }

        if (IsListed(sprite.Definition))
        {
            _listed.Add(sprite);
        }
    }

    [EngineRoutine(0x0042d770)]
    internal int DeleteFromHash(Sprite sprite)
    {
        int failed = 0;

        if (IsHashed(sprite.Definition))
        {
            int column = GetColumn(sprite.Position.X);
            int row = GetRow(sprite.Position.Y);
            var bucket = _buckets[row * _columns + column];

            if (column == _column && row == _row && _index > 0 && _index < bucket.Count && bucket[_index - 1] == sprite)
            {
                _index--;
            }

            failed = bucket.Remove(sprite) ? 0 : 1;
        }

        if (IsListed(sprite.Definition))
        {
            failed |= (_listed.Remove(sprite) ? 0 : 1) << 1;
        }

        var cursor = sprite.Map?.CursorSprite;

        if (failed != 0 && sprite != cursor && sprite != cursor?.Link)
        {
            sprite.Logger?.Error($"SPRITE {(int)sprite.Definition.Id}", LogError.Text, failed, "hash can't delete"u8);
        }

        return failed;
    }

    [EngineRoutine(0x0042d8c0)]
    internal void ChangeCoor(Sprite sprite, Vector2 position)
    {
        int oldColumn = GetColumn(sprite.Position.X);
        int oldRow = GetRow(sprite.Position.Y);
        int newColumn = GetColumn(position.X);
        int newRow = GetRow(position.Y);

        if ((oldColumn != newColumn || oldRow != newRow) && _buckets[oldRow * _columns + oldColumn].Remove(sprite))
        {
            _buckets[newRow * _columns + newColumn].Add(sprite);
        }
    }

    internal Sprite? First(Vector2 min, Vector2 max)
    {
        FirstHashInBox(min, max);

        return NextHashInBox();
    }

    [EngineRoutine(0x0042dd60)]
    internal void FirstHashInBox(Vector2 min, Vector2 max)
    {
        _firstColumn = GetColumn(min.X - _cell.X);
        _row = GetRow(min.Y - _cell.Y);
        _lastColumn = GetColumn(max.X + _cell.X);
        _lastRow = GetRow(max.Y + _cell.Y);
        _column = _firstColumn;
        _index = 0;
    }

    [EngineRoutine(0x0042deb0)]
    internal Sprite? NextHashInBox()
    {
        for (; _row <= _lastRow; _row++, _column = _firstColumn, _index = 0)
        {
            for (; _column <= _lastColumn; _column++, _index = 0)
            {
                var bucket = _buckets[(_row * _columns) + _column];

                if (_index < bucket.Count)
                    return bucket[_index++];
            }
        }

        return null;
    }

    private static float GetCellSize(float size)
    {
        int shift = 0;
        while (1 << shift < size * 0.5f)
        {
            shift++;
        }

        return 1 << shift;
    }

    private static int GetCell(float scaled, int count) => scaled < 0 ? 0 : scaled < count ? (int)scaled : count - 1;

    private int GetBucket(Vector2 point) => (GetRow(point.Y) * _columns) + GetColumn(point.X);

    private int GetColumn(float x) => GetCell(x * _scale.X, _columns);

    private int GetRow(float y) => GetCell(y * _scale.Y, _rows);
}
