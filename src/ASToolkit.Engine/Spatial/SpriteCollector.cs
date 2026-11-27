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
    private const int InstantRayStep = 16;

    private readonly Map _map;
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

    internal SpriteCollector(Map map)
    {
        _map = map;
        _buckets = [];
    }

    [EngineRoutine(0x0042d380)]
    internal SpriteCollector(SizeF mapSize, ReadOnlySpan<Vid?> vids, Map map)
    {
        _map = map;
        var largestObject = Vector2.Zero;
        foreach (var vid in vids)
        {
            if (vid is not null && IsHashed(vid))
            {
                largestObject = Vector2.Max(largestObject, vid.Size.AsVector2());
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
    public ReadOnlySpan<Sprite> Listed => _listed.Items;

    /// <summary>
    /// Gets half the width and depth of the largest object.
    /// </summary>
    public Vector2 LargestHalf { get; }

    internal SpriteList ListedSprites => _listed;

    /// <summary>
    /// Gets the sprites in the buckets a box covers.
    /// </summary>
    /// <param name="min">The top left corner of the box.</param>
    /// <param name="max">The bottom right corner of the box.</param>
    /// <returns>The query over the sprites.</returns>
    public HashQuery GetNear(Vector2 min, Vector2 max) => new(_buckets, _columns, GetColumn(min.X), GetColumn(max.X), GetRow(min.Y), GetRow(max.Y));

    internal static bool IsListed(Vid vid) => (vid.SpriteType & ListedTypes) != 0;

    internal static bool IsHashed(Vid vid) => vid.Properties.HasFlag(ObjectProperties.Hash);

    [EngineRoutine(0x0042d6a0)]
    internal void InsertToHash(Sprite sprite)
    {
        if (IsHashed(sprite.Vid))
        {
            _buckets[GetRow(sprite.Position.Y) * _columns + GetColumn(sprite.Position.X)].Insert(sprite);
        }

        if (IsListed(sprite.Vid))
        {
            _listed.Insert(sprite);
        }
    }

    [EngineRoutine(0x0042d770)]
    internal int DeleteFromHash(Sprite sprite)
    {
        int failed = 0;

        if (IsHashed(sprite.Vid))
        {
            int column = GetColumn(sprite.Position.X);
            int row = GetRow(sprite.Position.Y);
            var bucket = _buckets[row * _columns + column];

            if (column == _column && row == _row && _index > 0 && _index < bucket.Count && bucket[_index - 1] == sprite)
            {
                _index--;
            }

            failed = bucket.Delete(sprite) ? 0 : 1;
        }

        if (IsListed(sprite.Vid))
        {
            failed |= (_listed.Delete(sprite) ? 0 : 1) << 1;
        }

        var cursor = sprite.Map.Mouse;

        if (failed != 0 && sprite != cursor && sprite != cursor!.Link)
        {
            sprite.Map.Session.Error.Error($"SPRITE {(int)sprite.Vid.Id}", LogError.Text, failed, "hash can't delete"u8);
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

        if ((oldColumn != newColumn || oldRow != newRow) && _buckets[oldRow * _columns + oldColumn].Delete(sprite))
        {
            _buckets[newRow * _columns + newColumn].Insert(sprite);
        }
    }

    [EngineRoutine(0x0042da30)]
    internal Sprite? CanPlace(Vid vid, Vector3 point)
    {
        if (_map.GetGroundZForMoveTo(vid, point.AsVector2()) > point.Z)
            return _map.Mouse;

        if (vid.MoveMask == 0)
            return null;

        var half = vid.Half;
        float height = vid.Size.Z;
        for (var other = FirstHashInBox(point.AsVector2() - half, point.AsVector2() + half); other is not null; other = NextHashInBox())
        {
            var otherVid = other.Vid;
            if (other.Animation < SpriteAnimation.Death && MathF.Abs(other.Position.X - point.X) < otherVid.Half.X + half.X &&
                MathF.Abs(other.Position.Y - point.Y) < otherVid.Half.Y + half.Y && point.Z <= otherVid.Size.Z + other.Position.Z &&
                other.Position.Z <= point.Z + height && (vid.MoveMask & otherVid.MoveMask) != 0)
            {
                return other;
            }
        }

        return null;
    }

    [EngineRoutine(0x0042db40)]
    internal bool CheckLine(Vid vid, Vector3 from, ref Vector3 to)
    {
        if (vid.MoveMask == 0)
            return false;

        var position = new Point((int)from.X, (int)from.Y);
        int z = (int)from.Z;
        var distance = new Size((int)to.X - position.X, (int)to.Y - position.Y);
        var sign = new Size(Math.Sign(distance.Width), Math.Sign(distance.Height));
        distance = new Size(Math.Abs(distance.Width), Math.Abs(distance.Height));
        bool isXMajor = distance.Width >= distance.Height;
        int major = isXMajor ? distance.Width : distance.Height;
        int minor = isXMajor ? distance.Height : distance.Width;
        int error = -major;
        int heightStep = major == 0 ? 0 : ((int)to.Z - z) * InstantRayStep / major;
        for (int step = 0; step < major; step++)
        {
            error += 2 * minor;

            if (step > 0 && step % InstantRayStep == 0)
            {
                z += heightStep;
                var point = new Vector3(position.X, position.Y, z);

                if (CanPlace(vid, point) is not null)
                {
                    to = point;

                    return true;
                }
            }

            if (error >= 0)
            {
                if (isXMajor)
                {
                    position.Y += sign.Height;
                }
                else
                {
                    position.X += sign.Width;
                }

                error -= 2 * major;
            }

            if (isXMajor)
            {
                position.X += sign.Width;
            }
            else
            {
                position.Y += sign.Height;
            }
        }

        return false;
    }

    [EngineRoutine(0x0042dd60)]
    internal Sprite? FirstHashInBox(Vector2 min, Vector2 max)
    {
        _firstColumn = GetColumn(min.X - _cell.X);
        _row = GetRow(min.Y - _cell.Y);
        _lastColumn = GetColumn(max.X + _cell.X);
        _lastRow = GetRow(max.Y + _cell.Y);
        _column = _firstColumn;
        _index = 0;

        return NextHashInBox();
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

    private int GetColumn(float x) => GetCell(x * _scale.X, _columns);

    private int GetRow(float y) => GetCell(y * _scale.Y, _rows);
}
