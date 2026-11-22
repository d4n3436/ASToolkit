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

    private readonly Map _map;
    private readonly List<Sprite> _nearby = [];

    public DepthProbe(Map map)
    {
        _map = map;
    }

    public float MeasureHeight(Point pixel) => Math.Max(MeasureSceneryHeight(pixel), MeasureSpriteHeight(pixel));

    private int MeasureSceneryHeight(Point pixel)
    {
        int found = ClearHeight;
        const int cell = MovementMap.CellSize;
        for (int offset = 0; offset <= MaxSceneryHeight; offset += cell)
        {
            int height = (int)_map.Movement.GetHeight(new Vector2(pixel.X, pixel.Y + offset));

            if (height > found && (pixel.Y + height) / cell == (pixel.Y + offset) / cell)
            {
                found = height;
            }
        }

        return found;
    }

    private float MeasureSpriteHeight(Point pixel)
    {
        var point = new Vector2(pixel.X, pixel.Y);
        _nearby.Clear();
        foreach (var sprite in _map.Collector.GetNear(point - new Vector2(SpriteReach), point + new Vector2(SpriteReach, SpriteReach * 2)))
        {
            _nearby.Add(sprite);
        }

        var torso = _map.Weapons.Torso;
        if (torso is not null && !_nearby.Contains(torso))
        {
            _nearby.Add(torso);
        }

        float found = ClearHeight;
        foreach (var sprite in _nearby)
        {
            var mask = _map.Definitions.GetFrameMask(sprite.Definition, sprite.Frame);

            if (mask is null)
                continue;

            int left = (int)MathF.Round(sprite.Position.X - mask.Width / 2.0f);
            int top = (int)MathF.Round(sprite.Position.Y - sprite.Position.Z - mask.Height / 2.0f);
            int row = pixel.Y - top;

            if (!mask.Covers(pixel.X - left, row))
                continue;

            var obj = sprite.Definition.Object;
            int depth = (int)(sprite.Position.Z * DepthPerHeight);

            if (obj.Properties.HasFlag(ObjectProperties.AlwaysTop) && depth < RaisedDepth)
            {
                depth += RaisedDepth;
            }

            float height = (float)depth / DepthPerHeight + (obj.Size.Y < obj.Size.Z ? mask.Bottom - row : 0);
            found = Math.Max(found, height);
        }

        return found;
    }
}
