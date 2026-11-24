using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

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

    private SizeF _mapSize;
    private int _columns;
    private int _rows;
    private short[] _sceneryHeights = [];
    private short[] _objectHeights = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="MovementMap"/> class as a flat map.
    /// </summary>
    /// <param name="mapSize">The size of the map.</param>
    public MovementMap(SizeF mapSize)
    {
        Initialize(mapSize);
    }

    /// <summary>
    /// Gets the size of the map.
    /// </summary>
    public SizeF MapSize => _mapSize;

    internal int Columns => _columns;

    internal int Rows => _rows;

    internal ReadOnlySpan<short> SceneryHeights => _sceneryHeights;

    internal ReadOnlySpan<short> ObjectHeights => _objectHeights;

    /// <summary>
    /// Makes the map flat at a size, with every cell of both grids at height 0.
    /// </summary>
    /// <param name="mapSize">The size of the map.</param>
    [EngineRoutine(0x00410d70)]
    public void Initialize(SizeF mapSize)
    {
        _mapSize = mapSize;
        _columns = (int)_mapSize.Width / CellSize;
        _rows = (int)_mapSize.Height / CellSize;
        int cells = _columns * _rows;
        _sceneryHeights = new short[cells];
        _objectHeights = new short[cells];
    }

    /// <summary>
    /// Replaces the permanent grid, the one built terrain raises, with the heights of a map file's GRID chunk.
    /// </summary>
    /// <param name="block">The bytes of the chunk's record, two per cell in row order.</param>
    public void SetSceneryHeights(ReadOnlySpan<byte> block) => _sceneryHeights = MemoryMarshal.Cast<byte, short>(block).ToArray();

    /// <summary>
    /// Raises a cell of the permanent grid, the one built terrain raises, to a height.
    /// </summary>
    /// <param name="position">The position in map pixels, with the height to raise the cell to.</param>
    [EngineRoutine(0x00411260)]
    public void RaisePermanent(Vector3 position)
    {
        if (position.X < 0 || position.X >= _mapSize.Width || position.Y < 0 || position.Y >= _mapSize.Height)
            return;

        int index = (int)position.X / CellSize + (int)(position.Y * CellScale) * _columns;
        int value = (int)position.Z;

        if (_sceneryHeights[index] < value)
        {
            _sceneryHeights[index] = (short)value;
        }
    }

    /// <summary>
    /// Raises a cell of the temporary grid, the one every other solid sprite raises, to a height.
    /// </summary>
    /// <param name="position">The position in map pixels, with the height to raise the cell to.</param>
    [EngineRoutine(0x00411300)]
    public void RaiseTemporary(Vector3 position)
    {
        if (position.X < 0 || position.X >= _mapSize.Width || position.Y < 0 || position.Y >= _mapSize.Height)
            return;

        int index = (int)position.X / CellSize + (int)(position.Y * CellScale) * _columns;
        int value = (int)position.Z;

        if (_objectHeights[index] < value)
        {
            _objectHeights[index] = (short)value;
        }
    }

    /// <summary>
    /// Clears a cell of the temporary grid when it holds a height.
    /// </summary>
    /// <param name="position">The position in map pixels, with the height the cell was raised to.</param>
    [EngineRoutine(0x004113a0)]
    public void Lower(Vector3 position)
    {
        if (position.X < 0 || position.X >= _mapSize.Width || position.Y < 0 || position.Y >= _mapSize.Height)
            return;

        int index = (int)position.X / CellSize + (int)(position.Y * CellScale) * _columns;

        if (_objectHeights[index] == (int)position.Z)
        {
            _objectHeights[index] = 0;
        }
    }

    /// <summary>
    /// Returns the height of the cell under a point.
    /// </summary>
    /// <param name="point">The point.</param>
    /// <returns>The height.</returns>
    public float GetHeight(Vector2 point) => GetCellHeight(point);

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
        int ground = -16383;
        for (float row = top; row <= bottom; row += 1)
        {
            for (float column = left; column <= right; column += 1)
            {
                ground = Math.Max(ground, (int)GetCell((int)row, (int)column));
            }
        }

        return ground;
    }

    /// <summary>
    /// Determines whether an object can stand at a point.
    /// </summary>
    /// <param name="walker">The object.</param>
    /// <param name="point">The point.</param>
    /// <param name="ground">The height it stands at.</param>
    /// <returns><see langword="true"/> if it can stand there; otherwise, <see langword="false"/>.</returns>
    public bool CanStandOnGround(in GameObject walker, Vector3 point, out float ground)
    {
        ground = float.NaN;

        if (walker.MoveMask == 0)
        {
            return true;
        }

        var center = point.AsVector2();
        ground = GetSupportHeight(walker, center);

        if (!AllowsHeight(walker, ground, point.Z))
            return false;

        if (walker.SpriteClass == SpriteClass.Man)
            return true;

        var inset = walker.Size.AsVector2() / 2 - new Vector2(CornerInset);

        return AllowsHeight(walker, GetHeight(center + new Vector2(-inset.X, -inset.Y)), point.Z) && AllowsHeight(walker, GetHeight(center + inset with { Y = -inset.Y }), point.Z) &&
            AllowsHeight(walker, GetHeight(center + inset with { X = -inset.X }), point.Z) && AllowsHeight(walker, GetHeight(center + inset), point.Z);
    }

    /// <summary>
    /// Determines whether an object can stand at a point.
    /// </summary>
    /// <param name="walker">The object.</param>
    /// <param name="point">The point.</param>
    /// <returns><see langword="true"/> if it can stand there; otherwise, <see langword="false"/>.</returns>
    public bool CanStandOnGround(in GameObject walker, Vector3 point) => CanStandOnGround(walker, point, out _);

    /// <summary>
    /// Determines whether a point is at or above the ground an object stands on.
    /// </summary>
    /// <param name="gameObject">The object.</param>
    /// <param name="point">The point.</param>
    /// <returns><see langword="true"/> if the point is above the ground; otherwise, <see langword="false"/>.</returns>
    public bool IsAboveGround(in GameObject gameObject, Vector3 point) => point.Z >= GetSupportHeight(gameObject, point.AsVector2());

    private static float ClampToMap(float value, float size) => value < 0 ? 0 : value >= size ? size - 1 : value;

    private static bool AllowsHeight(in GameObject walker, float ground, float height) => walker.Properties.HasFlag(ObjectProperties.ZeroZ)
        ? ground - height <= walker.StepHeight && height - ground <= walker.DropHeight
        : ground <= height;

    [EngineRoutine(0x00410e60)]
    private float GetCellHeight(Vector2 point)
    {
        point.X = ClampToMap(point.X, _mapSize.Width);
        point.Y = ClampToMap(point.Y, _mapSize.Height);

        return GetCell((int)(point.Y * CellScale), (int)(point.X * CellScale));
    }

    private short GetCell(int row, int column)
    {
        int index = row * _columns + column;

        return Math.Max(_sceneryHeights[index], _objectHeights[index]);
    }
}
