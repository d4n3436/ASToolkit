using System.Numerics;
using ASToolkit.Core;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

internal sealed class Menu : SpriteList
{
    private const double MatchTolerance = 0.001;

    private readonly Map _map;

    [EngineRoutine(0x0043e120)]
    public Menu(Map map)
    {
        _map = map;
    }

    public Sprite? Current { get; set; }

    public MenuClicks Clicks { get; set; }

    public Sprite? LeftClick => Clicks.HasFlag(MenuClicks.Left) ? Current : null;

    public Sprite? RightClick => Clicks.HasFlag(MenuClicks.Right) ? Current : null;

    private static ReadOnlySpan<byte> MenuLogTag => "MENU"u8;

    [EngineRoutine(0x0043e140)]
    public bool Control(Input input)
    {
        Current = null;
        Clicks = MenuClicks.None;
        foreach (var sprite in Items)
        {
            var vid = sprite.Vid;

            if (vid.MoveMask == 0 || sprite.Animation is SpriteAnimation.Birth or >= SpriteAnimation.Death or SpriteAnimation.Close or SpriteAnimation.Open)
                continue;

            float top = sprite.Position.Y - sprite.Position.Z;
            var cursor = input.Cursor;
            float left = sprite.Position.X - vid.Half.X;
            float right = sprite.Position.X + vid.Half.X;
            float above = top - vid.Size.Z;
            float high = above - vid.Half.Y;
            float low = top + vid.Half.Y;
            bool isMissed = cursor.X < left || right < cursor.X || cursor.Y <= high || low <= cursor.Y ||
                Current is not null && sprite.Position.Z <= Current.Position.Z;
            if (isMissed)
            {
                sprite.ChangeAnimation((SpriteAnimation)((int)sprite.Animation & 1));
            }
            else
            {
                Current = sprite;
            }
        }

        if (Current is null)
            return false;

        if (input.Flags.HasFlag(InputFlags.LeftClick))
        {
            Clicks |= MenuClicks.Left;
            input.ClearLeftClick();
            Current.ChangeAnimation((SpriteAnimation)((int)Current.Animation & 1 | (int)MenuAnimation.PressDown));

            return true;
        }

        if (input.Flags.HasFlag(InputFlags.RightClick))
        {
            Clicks |= MenuClicks.Right;
            input.ClearRightClick();
        }

        int current = (int)Current.Animation;
        if ((current & ~1) != (int)MenuAnimation.PressDown)
        {
            Current.ChangeAnimation((SpriteAnimation)(current & 1 | (int)MenuAnimation.SelectDown));
        }

        return false;
    }

    [EngineRoutine(0x0043e2b0)]
    public void Load(ReadOnlySpan<byte> name)
    {
        if (!TryOpen(name, out var reader, out var header))
            return;

        if (reader.GoNext(Map.SpriteTag) == Resource.Found)
        {
            while (_map.TryLoadSprite(ref reader, header.Version, out var sprite))
            {
                if (sprite is not null)
                {
                    sprite.ChangeCoor(GetPosition(header, sprite.Position));
                    sprite.Action(ActionCode.Restore, ref reader, header.Version);
                }

                reader.GoNextSub(Map.SpriteTag);
            }

            return;
        }

        if (reader.GoBegin("SPRI"u8) == Resource.Found)
        {
            while (_map.TryLoadSprite(ref reader, header.Version, out var sprite))
            {
                sprite?.ChangeCoor(GetPosition(header, sprite.Position));
            }

            return;
        }

        _map.Session.Error.Error(MenuLogTag, LogError.Section, 0, "'SPR ' or 'SPRI' in menu"u8);
    }

    [EngineRoutine(0x0043e5c0)]
    public void DeleteFromFile(ReadOnlySpan<byte> name)
    {
        if (!TryOpen(name, out var reader, out var header))
            return;

        if (reader.GoNext(Map.SpriteTag) != Resource.Found)
        {
            _map.Session.Error.Error(MenuLogTag, LogError.Section, 0, "'SPR ' in MENU::DeleteFromFile"u8);
            return;
        }

        while (true)
        {
            int handle = reader.Read<int>();

            if (handle == Map.EndHandle)
                break;

            int id = reader.Read<int>();
            var position = reader.Read<Vector3>();
            for (int i = 0; i < Count; i++)
            {
                var sprite = this[i];
                var placed = GetPosition(header, position);

                if ((int)sprite.Vid.Id == id &&
                    MathF.Abs(sprite.Position.X - placed.X) < MatchTolerance &&
                    MathF.Abs(sprite.Position.Y - placed.Y) < MatchTolerance &&
                    MathF.Abs(sprite.Position.Z - position.Z) < MatchTolerance)
                {
                    DeleteSpriteNumber(i);
                    i--;
                }
            }

            reader.GoNextSub(Map.SpriteTag);
        }
    }

    [EngineRoutine(0x0043e880)]
    public int GetIdUnderCursor() => Current is null ? 0 : (int)Current.Vid.Id;

    [EngineRoutine(0x0043e8a0)]
    public int GetDirectionUnderCursor() =>
        Current is null ? 0 : Current.Vid.RealDirection(Current.Direction);

    private bool TryOpen(ReadOnlySpan<byte> name, out Resource reader, out MenuHeader header)
    {
        reader = new Resource(_map.Session.Error);
        header = default;
        if (reader.Open(_map.FileSystem, name, "MENU"u8) != 0)
        {
            _map.Session.Error.Error(MenuLogTag, LogError.Open, 0, name);
            return false;
        }

        if (reader.GoBegin(Map.HeadTag) != Resource.Found)
        {
            _map.Session.Error.Error(MenuLogTag, LogError.Section, 0, "'HEAD'in menu"u8);
            return false;
        }

        header = reader.Read<MenuHeader>();
        return true;
    }

    private Vector3 GetPosition(MenuHeader header, Vector3 position)
    {
        return new Vector3(_map.Session.Graphics.ScreenSize.Width * Map.HalfShare + (position.X - header.Origin.X - header.Size.Center.X),
            _map.Session.Graphics.ScreenSize.Height * Map.HalfShare + (position.Y - header.Origin.Y - header.Size.Center.Y), position.Z);
    }
}
