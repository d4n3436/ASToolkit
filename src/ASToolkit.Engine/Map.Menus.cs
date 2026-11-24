using System.Numerics;
using ASToolkit.Core;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public partial class Map
{
    private const int EndHandle = -1;
    private const int NoHandle = 0;
    private const double MenuMatchTolerance = 0.001;

    private readonly List<(int Handle, Sprite? Sprite)> _spriteRelations = [];

    internal static ReadOnlySpan<byte> MapLogTag => "MAP"u8;

    private static ReadOnlySpan<byte> MenuKind => "MENU"u8;

    private static ReadOnlySpan<byte> HeadTag => "HEAD"u8;

    private static ReadOnlySpan<byte> SpriteTag => "SPR "u8;

    private static ReadOnlySpan<byte> PackedSpriteTag => "SPRI"u8;

    private static ReadOnlySpan<byte> MenuLogTag => "MENU"u8;

    [EngineRoutine(0x0040ef30)]
    internal bool TryReadPointer(ref Resource reader, out Sprite? sprite)
    {
        sprite = null;

        if (!reader.TryRead(out int handle) || handle == EndHandle)
            return false;

        sprite = GetRelatedSprite(handle);

        return true;
    }

    private bool TryOpenMenu(ReadOnlySpan<byte> name, out Resource reader, out MenuHeader header)
    {
        reader = new Resource();
        header = default;
        if (reader.Open(_fileSystem, Session.Error, name, MenuKind) != 0)
        {
            Session.Error.Error(MenuLogTag, LogError.Open, 0, name);
            return false;
        }

        if (reader.GoBegin(HeadTag) != Resource.Found)
        {
            Session.Error.Error(MenuLogTag, LogError.Section, 0, "'HEAD'in menu"u8);
            return false;
        }

        header = reader.Read<MenuHeader>();
        return true;
    }

    [EngineRoutine(0x0043e2b0)]
    private void LoadMenu(ReadOnlySpan<byte> name)
    {
        if (!TryOpenMenu(name, out var reader, out var header))
            return;

        if (reader.GoNext(SpriteTag) == Resource.Found)
        {
            while (TryLoadSprite(ref reader, header.Version, out var sprite))
            {
                if (sprite is not null)
                {
                    sprite.ChangeCoor(GetMenuPosition(header, sprite.Position));
                    sprite.Action(ActionCode.Restore, ref reader, header.Version);
                }

                reader.GoNextSub(SpriteTag);
            }

            return;
        }

        if (reader.GoBegin(PackedSpriteTag) == Resource.Found)
        {
            while (TryLoadSprite(ref reader, header.Version, out var sprite))
            {
                sprite?.ChangeCoor(GetMenuPosition(header, sprite.Position));
            }

            return;
        }

        Session.Error.Error(MenuLogTag, LogError.Section, 0, "'SPR ' or 'SPRI' in menu"u8);
    }

    [EngineRoutine(0x0043e5c0)]
    private void DeleteMenuFromFile(ReadOnlySpan<byte> name)
    {
        if (!TryOpenMenu(name, out var reader, out var header))
            return;

        if (reader.GoNext(SpriteTag) != Resource.Found)
        {
            Session.Error.Error(MenuLogTag, LogError.Section, 0, "'SPR ' in MENU::DeleteFromFile"u8);
            return;
        }

        while (true)
        {
            int handle = reader.Read<int>();

            if (handle == EndHandle)
                break;

            int id = reader.Read<int>();
            var position = reader.Read<Vector3>();
            for (int i = 0; i < MenuList.Count; i++)
            {
                if (IsMenuElement(MenuList[i], header, id, position))
                {
                    DeleteMenuSpriteAt(i);
                    i--;
                }
            }

            reader.GoNextSub(SpriteTag);
        }
    }

    private Vector3 GetMenuPosition(MenuHeader header, Vector3 position)
    {
        return new Vector3(Session.ScreenSize.Width * HalfShare + (position.X - header.Origin.X - header.Size.Center.X),
            Session.ScreenSize.Height * HalfShare + (position.Y - header.Origin.Y - header.Size.Center.Y), position.Z);
    }

    private bool IsMenuElement(Sprite sprite, MenuHeader header, int id, Vector3 position)
    {
        var placed = GetMenuPosition(header, position);

        return (int)sprite.Vid.Id == id &&
            MathF.Abs(sprite.Position.X - placed.X) < MenuMatchTolerance &&
            MathF.Abs(sprite.Position.Y - placed.Y) < MenuMatchTolerance &&
            MathF.Abs(sprite.Position.Z - position.Z) < MenuMatchTolerance;
    }

    [EngineRoutine(0x0043df50)]
    private void DeleteMenuSpriteAt(int index)
    {
        if (index < 0 || index >= MenuList.Count)
            return;

        var sprite = MenuList[index];
        int last = MenuList.Count - 1;
        MenuList[index] = MenuList[last];
        MenuList.RemoveAt(last);

        int count = sprite.Release();

        if (count < 0)
        {
            Session.Error.Error($"SPRITE {(int)sprite.Vid.Id}", LogError.Invalid, count, "noRef at Release"u8);

            return;
        }

        DeleteSprite(sprite);
    }

    [EngineRoutine(0x0040f0a0)]
    private bool TryLoadSprite(ref Resource reader, int version, out Sprite? sprite)
    {
        sprite = null;

        if (!reader.TryRead(out int handle) || handle == EndHandle)
            return false;

        int id;
        Vector3 position;
        int direction;
        int army;
        if (version < MapHeader.FloatVersion)
        {
            var body = reader.Read<LegacySpriteBody>();
            id = body.Id;
            position = body.Position;
            direction = body.Direction;
            army = body.Army;
        }
        else
        {
            var body = reader.Read<SpriteBody>();
            id = body.Id;
            position = body.Position;
            direction = body.Direction;
            army = body.Army;
        }

        var vid = Vids.Find((KnownObject)id);
        if (vid is null)
        {
            Session.Error.Error(MapLogTag, LogError.Create, id, "sprite, this vid not exist"u8);
        }
        else
        {
            sprite = CreateSprite(vid, position, unchecked((byte)direction));
        }

        if (handle != NoHandle)
        {
            _spriteRelations.Add((handle, sprite));
        }

        if (sprite is not null)
        {
            sprite.ChangeArmy(army);
        }

        return true;
    }

    [EngineRoutine(0x0040ef70)]
    private bool TryLoadOldSprite(ref Resource reader, out Sprite? sprite)
    {
        sprite = null;

        if (!reader.TryRead(out int handle) || handle == EndHandle)
            return false;

        short id = 0;
        reader.ReadInto(ref id);
        short x = 0;
        reader.ReadInto(ref x);
        short y = 0;
        reader.ReadInto(ref y);
        short z = 0;
        reader.ReadInto(ref z);
        byte direction = 0;
        reader.ReadInto(ref direction);
        byte army = 0;
        reader.ReadInto(ref army);
        var vid = Vids.Find((KnownObject)id);
        if (vid is null)
        {
            Session.Error.Error(MapLogTag, LogError.Create, id, "sprite, this vid not exist"u8);
        }
        else
        {
            sprite = CreateSprite(vid, new Vector3(x, y, z), new Angle(direction));
        }

        if (handle != NoHandle)
        {
            _spriteRelations.Add((handle, sprite));
        }

        return true;
    }

    [EngineRoutine(0x00412b10)]
    internal Sprite? GetRelatedSprite(int handle)
    {
        for (int i = _spriteRelations.Count - 1; i >= 0; i--)
        {
            if (_spriteRelations[i].Handle == handle)
                return _spriteRelations[i].Sprite;
        }

        return null;
    }
}
