using System.Drawing;
using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public partial class Map
{
    internal const uint White = 0xffffffff;
    internal const uint Green = 0xff00ff00;
    internal const uint Red = 0xffff0000;
    private const uint Black = 0xff000000;
    private const uint Gray = 0xff808080;
    private const uint Blue = 0xff0000ff;
    private const uint LightBlue = 0xff8080ff;
    private const uint Yellow = 0xffffff00;
    private const float TextLineOffset = 1;
    private const float SoundCountInset = 20;
    private const int GridFirstColumnX = 12;
    private const int GridCellCenter = 4;

    internal Vector2 GetScreenPoint(Sprite sprite) => new(sprite.Position.X - Camera.X, sprite.Position.Y - sprite.Position.Z - Camera.Y);

    private static bool IsInViewport(Rectangle viewport, Vector2 point) => viewport.Left <= point.X && point.X < viewport.Right && viewport.Top <= point.Y && point.Y < viewport.Bottom;

    [EngineRoutine(0x0040b590)]
    internal virtual void DrawSecondaryInfo()
    {
        var graphics = Session.Graphics;
        var screen = graphics.Screen;

        if (_flags.HasFlag(MapFlags.DrawFps) && screen is not null)
        {
            graphics.DrawFormattedText(new Vector2(screen.Viewport.Left, screen.Viewport.Top + TextLineOffset), $"{_clock.FramesPerSecond}");
        }

        if (!Session.IsDebugMode || screen is null)
            return;

        if (_flags.HasFlag(MapFlags.DrawSoundCount))
        {
            graphics.DrawFormattedText(new Vector2(screen.Viewport.Right - SoundCountInset, screen.Viewport.Top + TextLineOffset), $"{Sound.PlayingCount,2}");
        }

        if (_flags.HasFlag(MapFlags.DrawGridZ))
        {
            DrawGridZ(screen);
        }

        if (_flags.HasFlag(MapFlags.DrawSpriteBoxes))
        {
            for (int layer = 0; layer < UpdateOrder.UpdatedLayerCount; layer++)
            {
                var sprites = _updates.GetLayer(layer);

                for (int i = sprites.Count - 1; i >= 0; i--)
                {
                    var sprite = sprites[i];

                    if (sprite is not null)
                    {
                        DrawSpriteBox(sprite);
                    }
                }
            }
        }

        if (_flags.HasFlag(MapFlags.DrawFlagman))
        {
            var flagman = Flagman(ControlledPlayer);
            var hovered = _players[ControlledPlayer].Hovered;
            var drawn = flagman ?? hovered ?? MenuCurrent;
            if (drawn is not null)
            {
                drawn.DrawSecondaryInfo();
            }
        }

        if (_flags.HasFlag(MapFlags.DrawGroups))
        {
            DrawGroups();
        }
    }

    private void DrawGridZ(Screen screen)
    {
        short[] scenery = _sceneryHeights;
        short[] objects = _objectHeights;
        int columns = _gridColumns;
        int rows = _gridRows;
        var viewport = screen.Viewport;

        for (int row = 1; row < rows; row++)
        {
            int x = GridFirstColumnX;

            for (int column = 1; column < columns; column++, x += GridZCellSize)
            {
                int index = row * columns + column;
                short height = Math.Max(scenery[index], objects[index]);
                short previousHeight = Math.Max(scenery[index - 1], objects[index - 1]);
                int top = row * GridZCellSize;
                var start = new Vector2(x - Camera.X, top - height + GridCellCenter - Camera.Y);
                var end = new Vector2(x - GridZCellSize - Camera.X, top - previousHeight + GridCellCenter - Camera.Y);

                if (!IsInViewport(viewport, start) && !IsInViewport(viewport, end))
                    continue;

                Session.Graphics.DrawLine(this, start, end, Gray);
            }
        }
    }

    [EngineRoutine(0x004455c0)]
    private void DrawSpriteBox(Sprite sprite)
    {
        var vid = sprite.Vid;
        var gameObject = vid.Object;

        if (vid.Class == SpriteClass.Region)
        {
            sprite.DrawSecondaryInfo();

            return;
        }

        var type = gameObject.SpriteType.ToOldSpriteType();
        uint color;

        if (type.HasFlag(OldSpriteType.Terrain) && gameObject.Properties.HasFlag(ObjectProperties.Hash))
        {
            color = Black;
        }
        else if (vid.Layer == ObjectLayer.Alpha)
        {
            color = White;
        }
        else if (type.HasFlag(OldSpriteType.Unit))
        {
            color = Green;
        }
        else if (type.HasFlag(OldSpriteType.Avia))
        {
            color = LightBlue;
        }
        else if (type.HasFlag(OldSpriteType.Object))
        {
            color = Yellow;
        }
        else if (type.HasFlag(OldSpriteType.Railway))
        {
            color = Red;
        }
        else if (type.HasFlag(OldSpriteType.Cannon))
        {
            color = Gray;
        }
        else if (vid.Class == SpriteClass.Frame)
        {
            color = White;
        }
        else
        {
            color = Red;
        }

        var position = sprite.Position;
        float bottom = position.Y - position.Z + vid.Half.Y - Camera.Y;
        float right = vid.Half.X + position.X - Camera.X;
        float top = position.Y - position.Z - vid.Half.Y - Camera.Y;
        float left = position.X - vid.Half.X - Camera.X;
        Session.Graphics.DrawRectangle(this, new Vector2(left, top), new Vector2(right, bottom), color);

        if ((type & (OldSpriteType.Object | OldSpriteType.Unit)) != 0)
        {
            float feet = position.Y - position.Z - Camera.Y;
            float roof = position.Y - position.Z - gameObject.Size.Z - Camera.Y;
            float x = position.X - Camera.X;
            Session.Graphics.DrawLine(this, new Vector2(x, roof), new Vector2(x, feet), Blue);
        }

        Session.Graphics.DrawFormattedText(new Vector2(position.X - Camera.X + TextLineOffset, position.Y - position.Z - Camera.Y), $"{(int)vid.Id}");
    }

    private void DrawGroups()
    {
        int index = 0;
        foreach (var group in Groups)
        {
            DrawGroup(group, index++);
        }
    }

    [EngineRoutine(0x0043ac40)]
    private void DrawGroup(Group group, int index)
    {
        foreach (var member in group.Members)
        {
            Session.Graphics.DrawFormattedText(GetScreenPoint(member), $"{index}");
        }
    }

}
