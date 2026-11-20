using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Scripting;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents the terrain heights of a map in 8 by 8 pixel cells.
/// </summary>
public sealed class MovementMap
{
    /// <summary>
    /// Represents the side of a cell in pixels.
    /// </summary>
    public const int CellSize = 8;
    private const float CellScale = 0.125f;
    private const float PlayerFootprintInset = 3;
    private const float CornerInset = 2;
    private const float DefaultMapSize = 8192;
    private readonly Dictionary<MergedMapSprite, List<RaisedCell>> _raised;
    private readonly SizeF _mapSize;
    private readonly int _columns;
    private readonly int _rows;
    private readonly short[] _sceneryHeights;
    private short[] _objectHeights;
    private bool _sharesObjectHeights;

    /// <summary>
    /// Creates a flat map.
    /// </summary>
    /// <param name="mapSize">The size of the map.</param>
    [EngineRoutine(0x00410d70)]
    public MovementMap(SizeF mapSize = default)
    {
        _mapSize = mapSize.IsEmpty ? new SizeF(DefaultMapSize, DefaultMapSize) : mapSize;
        _columns = (int)_mapSize.Width / CellSize;
        _rows = (int)_mapSize.Height / CellSize;
        int cells = _columns * _rows;
        _sceneryHeights = new short[cells];
        _objectHeights = new short[cells];
        _raised = new Dictionary<MergedMapSprite, List<RaisedCell>>(ReferenceEqualityComparer.Instance);
    }

    private MovementMap(MovementMap source)
    {
        _mapSize = source._mapSize;
        _columns = source._columns;
        _rows = source._rows;
        _raised = new Dictionary<MergedMapSprite, List<RaisedCell>>(source._raised, ReferenceEqualityComparer.Instance);
        _sceneryHeights = source._sceneryHeights;
        _objectHeights = source._objectHeights;
        _sharesObjectHeights = true;
        source._sharesObjectHeights = true;
    }

    /// <summary>
    /// Returns a copy of the map.
    /// </summary>
    /// <returns>The copy.</returns>
    public MovementMap Clone() => new(this);

    /// <summary>
    /// Creates the heights of a map's sprites.
    /// </summary>
    /// <param name="mapData">The map.</param>
    /// <param name="parts">The sprite parts.</param>
    public MovementMap(MapData mapData, SpritePartReader parts)
        : this(mapData.Size)
    {
        foreach (var sprite in mapData.Sprites)
        {
            Add(sprite, parts, SpriteProgram.TryParse(sprite.Payload, false, out var program) && HasDeathAction(program.GetStack(0)));
        }
    }

    /// <summary>
    /// Raises the cells under a sprite's parts.
    /// </summary>
    /// <param name="sprite">The sprite.</param>
    /// <param name="parts">The sprite parts.</param>
    /// <param name="removable">A value indicating whether the sprite can be removed later.</param>
    public void Add(MergedMapSprite sprite, SpritePartReader parts, bool removable)
    {
        var source = removable ? sprite : null;
        foreach (var part in parts.GetParts(sprite))
        {
            var obj = part.Object;

            if (obj.MaxHp > 0)
            {
                source = sprite;
            }

            bool scenery = source is null && obj.SpriteClass == SpriteClass.BuiltTerrain;

            if (part.Id != KnownObject.Crate &&
                (obj.SpriteType.HasFlag(SpriteType.Unit) || obj.SpriteType.HasFlag(SpriteType.Avia) ||
                 obj.SpriteType.HasFlag(SpriteType.Cannon) || obj.SpriteClass == SpriteClass.Man))
                continue;

            if (part.Id is KnownObject.SmallDoor or KnownObject.DoubleDoor or KnownObject.Fence)
                continue;

            if (obj.IsSolid && part.File is not null)
            {
                var file = part.File.Value;
                var size = new Size(file.Width, file.Height);
                var half = size / 2;
                var cells = size / CellSize * CellSize;
                foreach (var point in parts.GetHeightPoints(part))
                {
                    // Skip depth samples projected past the frame's last complete cell
                    if (point.X + half.Width >= cells.Width || point.Y + half.Height >= cells.Height)
                        continue;
                    if (scenery)
                    {
                        RaisePermanent(part.Position + point, source);
                    }
                    else
                    {
                        RaiseTemporary(part.Position + point, source);
                    }
                }
            }

            if (obj.Properties.HasFlag(ObjectProperties.BuildSizeToGridZ))
            {
                for (int y = 0; y <= (int)MathF.Ceiling(obj.Size.Y / CellSize); y++)
                for (int x = 0; x <= (int)MathF.Ceiling(obj.Size.X / CellSize); x++)
                {
                    var cell = new Vector3(
                        part.Position.X + Math.Min(x * CellSize, obj.Size.X) - obj.Size.X / 2,
                        part.Position.Y + Math.Min(y * CellSize, obj.Size.Y) - obj.Size.Y / 2,
                        part.Position.Z + obj.Size.Z);

                    if (scenery)
                    {
                        RaisePermanent(cell, source);
                    }
                    else
                    {
                        RaiseTemporary(cell, source);
                    }
                }
            }
        }
    }

    private static bool HasDeathAction(ReadOnlySpan<StackAction> actions)
    {
        foreach (var action in actions)
        {
            if ((int)action.Code is (int)SpriteAnimation.Death or (int)SpriteAnimation.Death2)
            {
                return true;
            }
        }

        return false;
    }

    private short[] GetWritableObjectHeights()
    {
        if (_sharesObjectHeights)
        {
            _objectHeights = (short[])_objectHeights.Clone();
            _sharesObjectHeights = false;
        }

        return _objectHeights;
    }

    /// <summary>
    /// Raises a cell of the permanent grid, the one built terrain raises, to a height.
    /// </summary>
    /// <param name="position">The position in map pixels, with the height to raise the cell to.</param>
    /// <param name="source">The sprite the height comes from, or null.</param>
    [EngineRoutine(0x00411260)]
    public void RaisePermanent(Vector3 position, MergedMapSprite? source = null)
    {
        if (position.X < 0 || position.X >= _mapSize.Width || position.Y < 0 || position.Y >= _mapSize.Height)
            return;

        int index = (int)position.X / CellSize + (int)(position.Y * CellScale) * _columns;
        short value = (short)(int)position.Z;

        if (_sceneryHeights[index] < value)
        {
            _sceneryHeights[index] = value;
        }

        Remember(source, index, value);
    }

    /// <summary>
    /// Raises a cell of the temporary grid, the one every other solid sprite raises, to a height.
    /// </summary>
    /// <param name="position">The position in map pixels, with the height to raise the cell to.</param>
    /// <param name="source">The sprite the height comes from, or null.</param>
    [EngineRoutine(0x00411300)]
    public void RaiseTemporary(Vector3 position, MergedMapSprite? source = null)
    {
        if (position.X < 0 || position.X >= _mapSize.Width || position.Y < 0 || position.Y >= _mapSize.Height)
            return;

        int index = (int)position.X / CellSize + (int)(position.Y * CellScale) * _columns;
        short value = (short)(int)position.Z;

        if (_objectHeights[index] < value)
        {
            GetWritableObjectHeights()[index] = value;
        }

        Remember(source, index, value);
    }

    private void Remember(MergedMapSprite? source, int index, short value)
    {
        if (source is null)
            return;

        ref var cells = ref CollectionsMarshal.GetValueRefOrAddDefault(_raised, source, out _);
        cells ??= [];
        cells.Add(new RaisedCell(index, value));
    }

    /// <summary>
    /// Gets the size of the map.
    /// </summary>
    public SizeF MapSize => _mapSize;

    /// <summary>
    /// Returns whether a sprite raises any cell.
    /// </summary>
    /// <param name="source">The sprite.</param>
    /// <returns><see langword="true"/> when the sprite raises cells; otherwise, <see langword="false"/>.</returns>
    public bool Raises(MergedMapSprite source) => _raised.ContainsKey(source);

    /// <summary>
    /// Lowers the cells a sprite raised.
    /// </summary>
    /// <param name="source">The sprite.</param>
    public void Remove(MergedMapSprite source)
    {
        if (_raised.TryGetValue(source, out var cells))
        {
            foreach (var cell in cells)
            {
                if (_objectHeights[cell.Index] == cell.Height)
                {
                    GetWritableObjectHeights()[cell.Index] = 0;
                }
            }
        }
    }

    [EngineRoutine(0x00410e60)]
    private float GetCellHeight(float x, float y)
    {
        x = ClampToMap(x, _mapSize.Width);
        y = ClampToMap(y, _mapSize.Height);

        return GetCell((int)(y * CellScale), (int)(x * CellScale));
    }

    private short GetCell(int row, int column)
    {
        int index = Math.Min(row, _rows - 1) * _columns + Math.Min(column, _columns - 1);

        return Math.Max(_sceneryHeights[index], _objectHeights[index]);
    }

    /// <summary>
    /// Returns the height of the cell under a point.
    /// </summary>
    /// <param name="point">The point.</param>
    /// <returns>The height.</returns>
    public float GetHeight(Vector2 point) => GetCellHeight(point.X, point.Y);

    /// <summary>
    /// Returns the height an object stands at over its footprint.
    /// </summary>
    /// <param name="walker">The object.</param>
    /// <param name="point">The point.</param>
    /// <returns>The height.</returns>
    [EngineRoutine(0x00411050)]
    public float GetSupportHeight(in GameObject walker, Vector2 point)
    {
        if (walker.SpriteClass != SpriteClass.Man)
        {
            return GetHeight(point);
        }

        var inset = walker.Size.AsVector2() * 0.5f - new Vector2(PlayerFootprintInset);
        float left = ClampToMap(point.X - inset.X, _mapSize.Width) * CellScale;
        float right = ClampToMap(point.X + inset.X, _mapSize.Width) * CellScale;
        float top = ClampToMap(point.Y - inset.Y, _mapSize.Height) * CellScale;
        float bottom = ClampToMap(point.Y + inset.Y, _mapSize.Height) * CellScale;
        short ground = 0;
        for (float row = top; row <= bottom; row += 1)
        for (float column = left; column <= right; column += 1)
        {
            ground = Math.Max(ground, GetCell((int)row, (int)column));
        }

        return ground;
    }

    private static float ClampToMap(float value, float size) => value < 0 ? 0 : value >= size ? size - 1 : value;

    /// <summary>
    /// Returns whether an object can stand at a point.
    /// </summary>
    /// <param name="walker">The object.</param>
    /// <param name="point">The point.</param>
    /// <param name="ground">The height it stands at.</param>
    /// <returns><see langword="true"/> when it can stand there; otherwise, <see langword="false"/>.</returns>
    public bool CanStandOnGround(in GameObject walker, Vector3 point, out float ground)
    {
        ground = float.NaN;

        if (walker.MoveMask == 0)
        {
            return true;
        }

        var half = walker.Size.AsVector2() / 2;
        ground = GetSupportHeight(walker, point.AsVector2());

        if (!AllowsHeight(walker, ground, point.Z))
        {
            return false;
        }

        if (walker.SpriteClass != SpriteClass.Man)
        {
            var inset = half - new Vector2(CornerInset);
            ReadOnlySpan<Vector2> corners = [new(-inset.X, -inset.Y), inset with { Y = -inset.Y }, inset with { X = -inset.X }, inset];
            foreach (var corner in corners)
            {
                if (!AllowsHeight(walker, GetHeight(point.AsVector2() + corner), point.Z))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Returns whether an object can stand at a point.
    /// </summary>
    /// <param name="walker">The object.</param>
    /// <param name="point">The point.</param>
    /// <returns><see langword="true"/> when it can stand there; otherwise, <see langword="false"/>.</returns>
    public bool CanStandOnGround(in GameObject walker, Vector3 point) => CanStandOnGround(walker, point, out _);

    /// <summary>
    /// Returns whether a point is at or above the ground an object stands on.
    /// </summary>
    /// <param name="obj">The object.</param>
    /// <param name="point">The point.</param>
    /// <returns><see langword="true"/> when the point is above the ground; otherwise, <see langword="false"/>.</returns>
    public bool IsAboveGround(in GameObject obj, Vector3 point) => point.Z >= GetSupportHeight(obj, point.AsVector2());

    private static bool AllowsHeight(in GameObject walker, float ground, float height) => walker.Properties.HasFlag(ObjectProperties.ZeroZ)
        ? ground - height <= walker.StepHeight && height - ground <= walker.DropHeight
        : ground <= height;

    private readonly record struct RaisedCell(int Index, short Height);
}
