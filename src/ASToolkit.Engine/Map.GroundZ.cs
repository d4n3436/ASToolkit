using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public sealed partial class Map
{
    /// <summary>
    /// Represents the side of a ground-height cell in pixels.
    /// </summary>
    public const int GridZCellSize = 8;
    
    private const float GridZCellScale = 0.125f;
    private const float PlayerFootprintInset = 3;
    private const float CornerInset = 2;

    private int _gridColumns;
    private int _gridRows;
    private short[] _sceneryHeights = [];
    private short[] _objectHeights = [];

    /// <summary>
    /// Returns the ground height of the cell under a point.
    /// </summary>
    /// <param name="point">The point.</param>
    /// <returns>The height.</returns>
    [EngineRoutine(0x00410e60)]
    public float GetGroundZ(Vector2 point)
    {
        point.X = ClampToMap(point.X, Size.Width);
        point.Y = ClampToMap(point.Y, Size.Height);

        return GetGridZCell((int)(point.Y * GridZCellScale), (int)(point.X * GridZCellScale));
    }

    /// <summary>
    /// Returns the ground height an object stands at over its footprint.
    /// </summary>
    /// <param name="walker">The object.</param>
    /// <param name="point">The point.</param>
    /// <returns>The height.</returns>
    [EngineRoutine(0x00411050)]
    public float GetGroundZForMoveTo(in GameObject walker, Vector2 point)
    {
        if (walker.SpriteClass != SpriteClass.Man)
        {
            return GetGroundZ(point);
        }

        var inset = walker.Size.AsVector2() * 0.5f - new Vector2(PlayerFootprintInset);
        float left = ClampToMap(point.X - inset.X, Size.Width) * GridZCellScale;
        float right = ClampToMap(point.X + inset.X, Size.Width) * GridZCellScale;
        float top = ClampToMap(point.Y - inset.Y, Size.Height) * GridZCellScale;
        float bottom = ClampToMap(point.Y + inset.Y, Size.Height) * GridZCellScale;
        int ground = -16383;
        for (float row = top; row <= bottom; row += 1)
        {
            for (float column = left; column <= right; column += 1)
            {
                ground = Math.Max(ground, (int)GetGridZCell((int)row, (int)column));
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
        ground = GetGroundZForMoveTo(walker, center);

        if (!AllowsHeight(walker, ground, point.Z))
            return false;

        if (walker.SpriteClass == SpriteClass.Man)
            return true;

        var inset = walker.Size.AsVector2() / 2 - new Vector2(CornerInset);

        return AllowsHeight(walker, GetGroundZ(center + new Vector2(-inset.X, -inset.Y)), point.Z) && AllowsHeight(walker, GetGroundZ(center + inset with { Y = -inset.Y }), point.Z) &&
            AllowsHeight(walker, GetGroundZ(center + inset with { X = -inset.X }), point.Z) && AllowsHeight(walker, GetGroundZ(center + inset), point.Z);
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
    public bool IsAboveGround(in GameObject gameObject, Vector3 point) => point.Z >= GetGroundZForMoveTo(gameObject, point.AsVector2());

    [EngineRoutine(0x00411260)]
    internal void SetGroundZ(Vector3 position)
    {
        if (position.X < 0 || position.X >= Size.Width || position.Y < 0 || position.Y >= Size.Height)
            return;

        int index = (int)position.X / GridZCellSize + (int)(position.Y * GridZCellScale) * _gridColumns;
        int value = (int)position.Z;

        if (_sceneryHeights[index] < value)
        {
            _sceneryHeights[index] = (short)value;
        }
    }

    [EngineRoutine(0x00411300)]
    internal void SetTempGroundZ(Vector3 position)
    {
        if (position.X < 0 || position.X >= Size.Width || position.Y < 0 || position.Y >= Size.Height)
            return;

        int index = (int)position.X / GridZCellSize + (int)(position.Y * GridZCellScale) * _gridColumns;
        int value = (int)position.Z;

        if (_objectHeights[index] < value)
        {
            _objectHeights[index] = (short)value;
        }
    }

    [EngineRoutine(0x004113a0)]
    internal void ClearTempGroundZ(Vector3 position)
    {
        if (position.X < 0 || position.X >= Size.Width || position.Y < 0 || position.Y >= Size.Height)
            return;

        int index = (int)position.X / GridZCellSize + (int)(position.Y * GridZCellScale) * _gridColumns;

        if (_objectHeights[index] == (int)position.Z)
        {
            _objectHeights[index] = 0;
        }
    }

    private static float ClampToMap(float value, float size) => value < 0 ? 0 : value >= size ? size - 1 : value;

    private static bool AllowsHeight(in GameObject walker, float ground, float height) => walker.Properties.HasFlag(ObjectProperties.ZeroZ)
        ? ground - height <= walker.StepHeight && height - ground <= walker.DropHeight
        : ground <= height;

    [EngineRoutine(0x00410d70)]
    private void ResetGroundZ()
    {
        _gridColumns = (int)Size.Width / GridZCellSize;
        _gridRows = (int)Size.Height / GridZCellSize;
        int cells = _gridColumns * _gridRows;
        _sceneryHeights = new short[cells];
        _objectHeights = new short[cells];
    }

    private short GetGridZCell(int row, int column)
    {
        int index = row * _gridColumns + column;

        return Math.Max(_sceneryHeights[index], _objectHeights[index]);
    }
}
