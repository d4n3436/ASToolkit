using System.Drawing;
using System.Numerics;

namespace ASToolkit.Engine;

public partial class Map
{
    internal const uint White = 0xffffffff;
    internal const uint Green = 0xff00ff00;
    internal const uint Red = 0xffff0000;
    internal const uint Black = 0xff000000;
    internal const uint Gray = 0xff808080;
    internal const uint Blue = 0xff0000ff;
    internal const uint LightBlue = 0xff8080ff;
    internal const uint Yellow = 0xffffff00;
    internal const float TextLineOffset = 1;
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

        if (!Constant.IsDebugMode || screen is null)
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
            for (int layer = 0; layer < UpdatedLayerCount; layer++)
            {
                var sprites = _layers[layer];

                for (int i = sprites.Count - 1; i >= 0; i--)
                {
                    var sprite = sprites[i];

                    sprite?.DrawSpriteBox();
                }
            }
        }

        if (_flags.HasFlag(MapFlags.DrawFlagman))
        {
            var flagman = Flagman(ControlledPlayer);
            var hovered = _players[ControlledPlayer].Hovered;
            var drawn = flagman ?? hovered ?? Menu.Current;
            drawn?.DrawSecondaryInfo();
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
