using System.Drawing;
using System.Numerics;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Scripting;

namespace ASToolkit.Core.Engine;

// Collision boxes do not use visual scale.
internal sealed class MovementMap
{
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

    public MovementMap Clone() => new(this);

    public MovementMap(LevelModel model, SpritePartReader parts) : this(model.MapSize)
    {
        foreach (var sprite in model.Sprites)
        {
            Add(sprite, parts, SpriteProgram.TryParse(sprite.Payload, false, out var program) && HasDeathAction(program.GetStack(0)));
        }
    }

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

            bool scenery = source is null && obj.SpriteClass == SpriteClass.Scenery;

            if (part.Id != KnownObject.Crate &&
                (obj.SpriteType.HasFlag(SpriteType.Unit) || obj.SpriteType.HasFlag(SpriteType.Avia) ||
                 obj.SpriteType.HasFlag(SpriteType.Cannon) || obj.SpriteClass == SpriteClass.Player))
                continue;

            if (part.Id is KnownObject.SmallDoor or KnownObject.DoubleDoor or KnownObject.Fence)
                continue;

            if (obj.IsSolid && part.File is not null)
            {
                var file = part.File.Value;
                foreach (var point in parts.GetHeightPoints(part))
                {
                    // Skip depth samples projected past the frame's last complete cell
                    if (point.X + file.Width / 2 >= file.Width / CellSize * CellSize ||
                        point.Y + file.Height / 2 >= file.Height / CellSize * CellSize)
                        continue;
                    Raise(part.Position.X + point.X, part.Position.Y + point.Y, part.Position.Z + point.Z, scenery, source);
                }
            }

            if (obj.Flags.HasFlag(ObjectFlags.Outline))
            {
                for (int y = 0; y <= (int)MathF.Ceiling(obj.Size.Y / CellSize); y++)
                for (int x = 0; x <= (int)MathF.Ceiling(obj.Size.X / CellSize); x++)
                {
                    Raise(part.Position.X + Math.Min(x * CellSize, obj.Size.X) - obj.Size.X / 2,
                        part.Position.Y + Math.Min(y * CellSize, obj.Size.Y) - obj.Size.Y / 2, part.Position.Z + obj.Size.Z, scenery, source);
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

    [EngineRoutine(0x00411300)]
    [EngineRoutine(0x00411260)]
    public void Raise(float x, float y, float height, bool isScenery = false, MergedMapSprite? source = null)
    {
        if (x < 0 || x >= _mapSize.Width || y < 0 || y >= _mapSize.Height)
            return;

        int index = (int)x / CellSize + (int)(y * CellScale) * _columns;
        short value = (short)(int)height;
        short[] grid = isScenery ? _sceneryHeights : _objectHeights;

        if (grid[index] < value)
        {
            (isScenery ? grid : GetWritableObjectHeights())[index] = value;
        }

        if (source is null)
            return;

        if (!_raised.TryGetValue(source, out var cells))
        {
            _raised[source] = cells = [];
        }
        cells.Add(new RaisedCell(index, value));
    }


    public SizeF MapSize => _mapSize;

    public bool Raises(MergedMapSprite source) => _raised.ContainsKey(source);

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

    public float GetHeight(Vector2 point) => GetCellHeight(point.X, point.Y);

    [EngineRoutine(0x00411050)]
    public float GetSupportHeight(in GameObject walker, Vector2 point)
    {
        if (walker.SpriteClass != SpriteClass.Player)
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

        if (walker.SpriteClass != SpriteClass.Player)
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

    public bool CanStandOnGround(in GameObject walker, Vector3 point) => CanStandOnGround(walker, point, out _);

    public bool IsAboveGround(in GameObject obj, Vector3 point) => point.Z >= GetSupportHeight(obj, point.AsVector2());

    private static bool AllowsHeight(in GameObject walker, float ground, float height) => walker.Flags.HasFlag(ObjectFlags.FollowsTerrain)
        ? ground - height <= walker.StepHeight && height - ground <= walker.DropHeight
        : ground <= height;

    private readonly record struct RaisedCell(int Index, short Height);
}
