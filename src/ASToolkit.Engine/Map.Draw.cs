using System.Drawing;
using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public partial class Map
{
    private const float LayerHalf = 0.5f;
    private const int CullWidthMask = unchecked((int)0xfffff800);
    private const int CullHeightMask = unchecked((int)0xfffffc00);
    private static readonly Size CullHalfSize = new(0x400, 0x200);

    /// <summary>
    /// Draws the sprites of one layer, with the sprites of the cursor's link chain that belong to the layer drawn last.
    /// </summary>
    /// <param name="screen">The screen.</param>
    /// <param name="camera">The top-left corner of the view on the map.</param>
    /// <param name="layer">The layer, from ground to cursor.</param>
    [EngineRoutine(0x0040f7f0)]
    public void DrawLayer(Screen screen, Vector2 camera, ObjectLayer layer)
    {
        var sprites = _layers[(int)layer];

        if (layer is ObjectLayer.Ground or ObjectLayer.Region)
        {
            for (int i = sprites.Count - 1; i >= 0; i--)
            {
                var sprite = sprites[i];

                if (sprite is not null && !sprite.IsInvisible)
                {
                    DrawSprite(screen, sprite, camera);
                }
            }
        }
        else
        {
            var center = new Point((int)(screen.Size.Width * LayerHalf + camera.X), (int)(screen.Size.Height * LayerHalf + camera.Y));

            for (int i = sprites.Count - 1; i >= 0; i--)
            {
                var sprite = sprites[i];

                if (sprite is null || sprite.IsInvisible)
                    continue;

                var point = new Point((int)sprite.Position.X, (int)(sprite.Position.Y - sprite.Position.Z));
                bool isNear = ((point.X - center.X + CullHalfSize.Width) & CullWidthMask) == 0 && ((point.Y - center.Y + CullHalfSize.Height) & CullHeightMask) == 0;

                if (isNear || (int)sprite.Position.Y - center.Y >= CullHalfSize.Height)
                {
                    DrawSprite(screen, sprite, camera);
                }
            }
        }

        if (!CursorSprite.IsHardwareCursor && !CursorSprite.Vid.Object.Properties.HasFlag(ObjectProperties.AlwaysTop))
        {
            DrawCursorChain(screen, camera, layer);
        }
    }

    /// <summary>
    /// Draws the cursor and the sprites of its link chain, all of them or those of one layer.
    /// </summary>
    /// <param name="screen">The screen.</param>
    /// <param name="camera">The top-left corner of the view on the map.</param>
    /// <param name="layer">The layer whose chain sprites are drawn, or null to draw the whole chain.</param>
    public void DrawCursorChain(Screen screen, Vector2 camera, ObjectLayer? layer)
    {
        DrawChainSprite(screen, camera, CursorSprite, layer);

        foreach (var link in CursorSprite.EnumerateLinks())
        {
            DrawChainSprite(screen, camera, link, layer);
        }
    }

    private void DrawChainSprite(Screen screen, Vector2 camera, Sprite sprite, ObjectLayer? layer)
    {
        if ((layer is null || sprite.Vid.Layer == layer) && !sprite.IsInvisible)
        {
            DrawSprite(screen, sprite, camera);
        }
    }

    private void DrawSprite(Screen screen, Sprite sprite, Vector2 camera) => sprite.Draw(screen, this, camera);
}
