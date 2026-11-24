using System.Drawing;
using System.Globalization;
using System.Numerics;
using System.Text;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public sealed partial class Map
{
    private const uint Black = 0xff000000;
    private const uint White = 0xffffffff;
    private const uint Gray = 0xff808080;
    private const uint Blue = 0xff0000ff;
    private const uint LightBlue = 0xff8080ff;
    private const uint Green = 0xff00ff00;
    private const uint Red = 0xffff0000;
    private const uint Yellow = 0xffffff00;
    private const float TextLineOffset = 1;
    private const float SoundCountInset = 20;
    private const int GridCellSize = 8;
    private const int GridFirstColumnX = 12;
    private const int GridCellCenter = 4;
    private const float InfoTextLeft = 22;
    private const float InfoLineHeight = 12;
    private const float OrdersTextLeft = 30;
    private const float SpeedScale = 1000;
    private const float RegionBoxMargin = 1;

    [EngineRoutine(0x00405af0)]
    internal void DrawOverlay()
    {
        DrawDebugInfo();

        if (_debug.HasFlag(StepFlags.DrawHashedNumbers))
        {
            for (int index = Collector.Listed.Length - 1; index >= 0; index = Math.Min(Collector.Listed.Length, index) - 1)
            {
                var sprite = Collector.Listed[index];

                if (sprite.Owner is null)
                {
                    Session.Graphics.DrawFormattedText(GetScreenPoint(sprite), $"{sprite.Hp}");
                }
            }
        }

        if (_debug.HasFlag(StepFlags.DrawHashedSprites))
        {
            for (int index = Collector.Listed.Length - 1; index >= 0; index = Math.Min(Collector.Listed.Length, index) - 1)
            {
                DrawGoalLines(Collector.Listed[index]);
            }
        }
    }

    private static bool IsInViewport(Rectangle viewport, Vector2 point) => viewport.Left <= point.X && point.X < viewport.Right && viewport.Top <= point.Y && point.Y < viewport.Bottom;

    private static int GetDefinitionId(Sprite? sprite) => sprite is null ? 0 : (int)sprite.Definition.Id;

    [EngineRoutine(0x0040b590)]
    private void DrawDebugInfo()
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
            var flagman = GetFlagman(ControlledPlayer);
            var hovered = _players[ControlledPlayer].Hovered;
            var drawn = flagman ?? hovered ?? _menuCurrent;
            if (drawn is not null)
            {
                DrawInfo(drawn);
            }
        }

        if (_flags.HasFlag(MapFlags.DrawGroups))
        {
            DrawGroups();
        }
    }

    private void DrawGridZ(Screen screen)
    {
        var scenery = Movement.SceneryHeights;
        var objects = Movement.ObjectHeights;
        int columns = Movement.Columns;
        int rows = Movement.Rows;
        var viewport = screen.Viewport;

        for (int row = 1; row < rows; row++)
        {
            int x = GridFirstColumnX;

            for (int column = 1; column < columns; column++, x += GridCellSize)
            {
                int index = row * columns + column;
                short height = Math.Max(scenery[index], objects[index]);
                short previousHeight = Math.Max(scenery[index - 1], objects[index - 1]);
                int top = row * GridCellSize;
                var start = new Vector2(x - Camera.X, top - height + GridCellCenter - Camera.Y);
                var end = new Vector2(x - GridCellSize - Camera.X, top - previousHeight + GridCellCenter - Camera.Y);

                if (!IsInViewport(viewport, start) && !IsInViewport(viewport, end))
                    continue;

                Session.Graphics.DrawLine(this, start, end, Gray);
            }
        }
    }

    [EngineRoutine(0x004455c0)]
    private void DrawSpriteBox(Sprite sprite)
    {
        var definition = sprite.Definition;
        var gameObject = definition.Object;

        if (definition.Class == SpriteClass.Region)
        {
            DrawInfo(sprite);

            return;
        }

        var type = gameObject.SpriteType.ToOldSpriteType();
        uint color;

        if (type.HasFlag(OldSpriteType.Terrain) && gameObject.Properties.HasFlag(ObjectProperties.Hash))
        {
            color = Black;
        }
        else if (definition.Layer == ObjectLayer.Alpha)
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
        else if (definition.Class == SpriteClass.Frame)
        {
            color = White;
        }
        else
        {
            color = Red;
        }

        var position = sprite.Position;
        float bottom = position.Y - position.Z + definition.Half.Y - Camera.Y;
        float right = definition.Half.X + position.X - Camera.X;
        float top = position.Y - position.Z - definition.Half.Y - Camera.Y;
        float left = position.X - definition.Half.X - Camera.X;
        Session.Graphics.DrawRectangle(this, new Vector2(left, top), new Vector2(right, bottom), color);

        if ((type & (OldSpriteType.Object | OldSpriteType.Unit)) != 0)
        {
            float feet = position.Y - position.Z - Camera.Y;
            float roof = position.Y - position.Z - gameObject.Size.Z - Camera.Y;
            float x = position.X - Camera.X;
            Session.Graphics.DrawLine(this, new Vector2(x, roof), new Vector2(x, feet), Blue);
        }

        Session.Graphics.DrawFormattedText(new Vector2(position.X - Camera.X + TextLineOffset, position.Y - position.Z - Camera.Y), $"{(int)definition.Id}");
    }

    private void DrawInfo(Sprite sprite)
    {
        if (sprite is RegionSprite region)
        {
            DrawRegionInfo(region);
        }
        else if (sprite.Definition.Class != SpriteClass.Primitive)
        {
            DrawSpriteInfo(sprite);
        }
    }

    [EngineRoutine(0x00442660)]
    private void DrawSpriteInfo(Sprite sprite)
    {
        var graphics = Session.Graphics;
        var view = graphics.Viewport;
        float y = view.Top;
        var position = sprite.Position;
        int ammo = Action(sprite, ActionCode.GetAmmo);
        graphics.DrawFormattedText(new Vector2(view.Left + InfoTextLeft, y), $"Ref={sprite.ReferenceCount,-3} cmd={(int)sprite.Command,1} ani={(int)sprite.Animation,-2} hp={sprite.Hp,-3} AT={(int)sprite.AttackResult} goal={GetDefinitionId(sprite.Goal),-3} spd={(int)(sprite.Speed * SpeedScale),-3},{(int)(sprite.VerticalSpeed * SpeedScale),-3} timer={sprite.Timer} ammo={ammo} mvE={(sprite.HasReachedX ? 1 : 0),1}{(sprite.HasReachedY ? 1 : 0),1} {(int)position.X},{(int)position.Y},{(int)position.Z}");

        var link = sprite.Link;
        if (link is not null && link.Definition == sprite.Definition.LinkDefinition)
        {
            y += InfoLineHeight;
            int linkAmmo = Action(link, ActionCode.GetAmmo);
            graphics.DrawFormattedText(new Vector2(view.Left + InfoTextLeft, y), $"Ref={link.ReferenceCount,-3} cmd={(int)link.Command,1} ani={(int)link.Animation,-2} hp={link.Hp,-3} AT={(int)link.AttackResult} goal={GetDefinitionId(link.Goal),-3} timer={link.Timer} ammo={linkAmmo}");
        }

        var orders = sprite.ActionStack;
        if (orders.Count != 0)
        {
            y += InfoLineHeight;
            var text = new StringBuilder();
            text.Append(CultureInfo.InvariantCulture, $"{orders.Count} - ");
            for (int i = orders.Count - 1; i >= 0; i--)
            {
                var order = orders[i];
                text.Append(CultureInfo.InvariantCulture, $"{order.Value}({order.Var1},{order.Var2},{order.Var3}) ");
            }

            graphics.DrawText(new Vector2(view.Left + OrdersTextLeft, y), GameTextEncoding.Cyrillic.GetBytes(text.ToString()), White);
        }

        DrawGoalLines(sprite);
    }

    [EngineRoutine(0x00449730)]
    private void DrawRegionInfo(RegionSprite region)
    {
        var position = region.Position;
        var size = region.Size;
        float left = position.X - size.X * HalfShare - Camera.X;
        float top = position.Y - position.Z - size.Y * HalfShare - Camera.Y;
        float right = size.X * HalfShare + position.X - Camera.X;
        float bottom = size.Y * HalfShare + (position.Y - position.Z) - Camera.Y;
        Session.Graphics.DrawRectangle(this, new Vector2(left - RegionBoxMargin, top - RegionBoxMargin), new Vector2(right + RegionBoxMargin, bottom + RegionBoxMargin), White);
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

    [EngineRoutine(0x00442900)]
    private void DrawGoalLines(Sprite sprite)
    {
        var graphics = Session.Graphics;

        if (sprite.Goal is not null)
        {
            var start = GetScreenPoint(sprite);
            var end = GetScreenPoint(sprite.Goal);
            graphics.DrawLine(this, start, end, Green);
        }

        if (sprite.Link?.Goal is not null)
        {
            var start = GetScreenPoint(sprite.Link);
            var end = GetScreenPoint(sprite.Link.Goal);
            graphics.DrawLine(this, start, end, Red);
        }
    }

    private Vector2 GetScreenPoint(Sprite sprite) => new(sprite.Position.X - Camera.X, sprite.Position.Y - sprite.Position.Z - Camera.Y);
}
