using System.Drawing;
using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal sealed class DepthProbe
{
    private const int ClearHeight = -1;
    private const int MaxSceneryHeight = 512;
    private const int SpriteReach = 256;
    private const int DepthPerHeight = 8;
    private const int RaisedDepth = 0x3fff;

    private readonly GameWorld _world;
    private readonly List<Unit> _nearby = [];

    public DepthProbe(GameWorld world)
    {
        _world = world;
    }

    public float MeasureHeight(Point pixel) => Math.Max(MeasureSceneryHeight(pixel), MeasureUnitHeight(pixel));

    // Walks down the screen column, a scenery pixel of height h raising the height cell h below it
    private int MeasureSceneryHeight(Point pixel)
    {
        int found = ClearHeight;
        const int cell = MovementMap.CellSize;
        for (int offset = 0; offset <= MaxSceneryHeight; offset += cell)
        {
            int height = (int)_world.Movement.GetHeight(new Vector2(pixel.X, pixel.Y + offset));

            if (height > found && (pixel.Y + height) / cell == (pixel.Y + offset) / cell)
            {
                found = height;
            }
        }

        return found;
    }

    private float MeasureUnitHeight(Point pixel)
    {
        var point = new Vector2(pixel.X, pixel.Y);
        _world.Units.Collect(point - new Vector2(SpriteReach), point + new Vector2(SpriteReach, SpriteReach * 2), _nearby);

        if (!_nearby.Contains(_world.Weapons.Torso))
        {
            _nearby.Add(_world.Weapons.Torso);
        }

        float found = ClearHeight;
        foreach (var unit in _nearby)
        {
            var mask = _world.Kinds.GetFrameMask(unit.Kind, unit.Frame);

            if (mask is null)
                continue;

            int left = (int)MathF.Round(unit.Position.X - mask.Width / 2.0f);
            int top = (int)MathF.Round(unit.Position.Y - unit.Position.Z - mask.Height / 2.0f);
            int row = pixel.Y - top;

            if (!mask.Covers(pixel.X - left, row))
                continue;

            var obj = unit.Kind.Object;
            int depth = (int)(unit.Position.Z * DepthPerHeight);

            if (obj.Flags.HasFlag(ObjectFlags.Raised) && depth < RaisedDepth)
            {
                depth += RaisedDepth;
            }

            float height = (float)depth / DepthPerHeight + (obj.Size.Y < obj.Size.Z ? mask.Bottom - row : 0);
            found = Math.Max(found, height);
        }

        return found;
    }
}
