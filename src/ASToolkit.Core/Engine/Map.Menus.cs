using System.Drawing;
using System.Numerics;
using ASToolkit.Core.Engine.IO;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

public sealed partial class Map
{
    private const int EndHandle = -1;
    private const int NoHandle = 0;
    private const int ItemsVersion = 12;
    private const int WordItemsVersion = 10;
    private const int IntItemsVersion = 11;
    private const double MenuMatchTolerance = 0.001;

    private readonly List<(int Handle, Sprite? Sprite)> _spriteRelations = [];

    private static ReadOnlySpan<byte> MenuKind => "MENU"u8;

    private static ReadOnlySpan<byte> HeadTag => "HEAD"u8;

    private static ReadOnlySpan<byte> SpriteTag => "SPR "u8;

    private static ReadOnlySpan<byte> PackedSpriteTag => "SPRI"u8;

    private static ReadOnlySpan<byte> MenuLogTag => "MENU"u8;

    private static ReadOnlySpan<byte> MapLogTag => "MAP"u8;

    [EngineRoutine(0x0043e2b0)]
    private void LoadMenu(ReadOnlySpan<byte> name)
    {
        var reader = new Resource();
        if (reader.Open(_fileSystem, Session.Logger, name, MenuKind) != 0)
        {
            Session.Logger.Error(MenuLogTag, LogError.Open, 0, name);
            return;
        }

        if (reader.FindChunk(HeadTag) != Resource.Found)
        {
            Session.Logger.Error(MenuLogTag, LogError.Section, 0, "'HEAD'in menu"u8);
            return;
        }

        var header = reader.Read<MenuHeader>();
        if (reader.FindNextChunk(SpriteTag) == Resource.Found)
        {
            while (TryLoadSprite(ref reader, header.Version, out var sprite))
            {
                if (sprite is not null)
                {
                    sprite.ChangeCoor(GetMenuPosition(header, sprite.Position));
                    Restore(sprite, ref reader, header.Version);
                }

                reader.NextRecord(SpriteTag);
            }

            return;
        }

        if (reader.FindChunk(PackedSpriteTag) == Resource.Found)
        {
            while (TryLoadSprite(ref reader, header.Version, out var sprite))
            {
                sprite?.ChangeCoor(GetMenuPosition(header, sprite.Position));
            }

            return;
        }

        Session.Logger.Error(MenuLogTag, LogError.Section, 0, "'SPR ' or 'SPRI' in menu"u8);
    }

    [EngineRoutine(0x0043e5c0)]
    private void DeleteMenuFromFile(ReadOnlySpan<byte> name)
    {
        var reader = new Resource();
        if (reader.Open(_fileSystem, Session.Logger, name, MenuKind) != 0)
        {
            Session.Logger.Error(MenuLogTag, LogError.Open, 0, name);
            return;
        }

        if (reader.FindChunk(HeadTag) != Resource.Found)
        {
            Session.Logger.Error(MenuLogTag, LogError.Section, 0, "'HEAD'in menu"u8);
            return;
        }

        var header = reader.Read<MenuHeader>();
        if (reader.FindNextChunk(SpriteTag) != Resource.Found)
        {
            Session.Logger.Error(MenuLogTag, LogError.Section, 0, "'SPR ' in MENU::DeleteFromFile"u8);
            return;
        }

        while (true)
        {
            int handle = reader.Read<int>();

            if (handle == EndHandle)
                break;

            int id = reader.Read<int>();
            var position = new Vector3(reader.Read<float>(), reader.Read<float>(), reader.Read<float>());
            for (int i = 0; i < _menuSprites.Count; i++)
            {
                if (IsMenuElement(_menuSprites[i], header, id, position))
                {
                    DeleteMenuSpriteAt(i);
                    i--;
                }
            }

            reader.NextRecord(SpriteTag);
        }
    }

    private static Size GetHalfSize(MenuHeader header) => new(header.Size.Width / 2, header.Size.Height / 2);

    private Vector3 GetMenuPosition(MenuHeader header, Vector3 position)
    {
        var screen = Session.ScreenSize;
        var half = GetHalfSize(header);
        double x = (double)position.X - header.Origin.X - half.Width + (double)screen.Width * HalfShare;
        double y = (double)position.Y - header.Origin.Y - half.Height + (double)screen.Height * HalfShare;

        return new Vector3((float)x, (float)y, position.Z);
    }

    private bool IsMenuElement(Sprite sprite, MenuHeader header, int id, Vector3 position)
    {
        var screen = Session.ScreenSize;
        var half = GetHalfSize(header);

        return (int)sprite.Definition.Id == id &&
            Math.Abs(sprite.Position.X - ((double)position.X - header.Origin.X - half.Width + (double)screen.Width * HalfShare)) < MenuMatchTolerance &&
            Math.Abs(sprite.Position.Y - ((double)position.Y - header.Origin.Y - half.Height + (double)screen.Height * HalfShare)) < MenuMatchTolerance &&
            Math.Abs((double)sprite.Position.Z - position.Z) < MenuMatchTolerance;
    }

    [EngineRoutine(0x0043df50)]
    private void DeleteMenuSpriteAt(int index)
    {
        var sprite = _menuSprites[index];
        int last = _menuSprites.Count - 1;
        _menuSprites[index] = _menuSprites[last];
        _menuSprites.RemoveAt(last);

        DestroySprite(sprite);
    }

    [EngineRoutine(0x0040f0a0)]
    private bool TryLoadSprite(ref Resource reader, int version, out Sprite? sprite)
    {
        sprite = null;

        if (!reader.TryRead(out int handle) || handle == EndHandle)
            return false;

        int id = reader.Read<int>();
        var position = version < MapHeader.FloatVersion
            ? new Vector3(reader.Read<int>(), reader.Read<int>(), reader.Read<int>())
            : new Vector3(reader.Read<float>(), reader.Read<float>(), reader.Read<float>());

        int direction = reader.Read<int>();
        int army = reader.Read<int>();
        var definition = Definitions.Find((KnownObject)id);
        if (definition is null)
        {
            Session.Logger.Error(MapLogTag, LogError.Create, id, "sprite, this vid not exist"u8);
        }
        else
        {
            sprite = CreateSprite(definition, position, unchecked((byte)direction));
        }

        if (handle != NoHandle)
        {
            _spriteRelations.Add((handle, sprite));
        }

        if (sprite is not null)
        {
            ChangeArmy(sprite, army);
        }

        return true;
    }

    [EngineRoutine(0x00412b10)]
    private Sprite? GetRelatedSprite(int handle)
    {
        for (int i = _spriteRelations.Count - 1; i >= 0; i--)
        {
            if (_spriteRelations[i].Handle == handle)
                return _spriteRelations[i].Sprite;
        }

        return null;
    }

    private void Restore(Sprite sprite, ref Resource reader, int version)
    {
        switch (sprite.Definition.Object.SpriteClass)
        {
            case SpriteClass.Unit or SpriteClass.Creature or SpriteClass.Building or SpriteClass.Avia or SpriteClass.Man:
                RestoreUnit(sprite, ref reader, version);
                break;
            case SpriteClass.Text when sprite.Text is not null:
                RestoreText(sprite, ref reader, version);
                break;
            default:
                RestoreSprite(sprite, ref reader, version);
                break;
        }
    }

    private void RestoreSprite(Sprite sprite, ref Resource reader, int version)
    {
        int count = sprite.ActionStack.Count;
        reader.ReadInto(ref count);
        LoadOrders(sprite, reader.Read<StackAction>(count), GetRelatedSprite);

        if (version < ItemsVersion)
            return;

        int itemCount = reader.Read<int>();
        var items = reader.Read<int>(itemCount);
        if (itemCount != 0)
        {
            sprite.Items.Clear();
            sprite.Items.AddRange(items);
        }
    }

    private void RestoreUnit(Sprite sprite, ref Resource reader, int version)
    {
        RestoreSprite(sprite, ref reader, version);
        int behavior = (int)sprite.Behavior;
        reader.ReadInto(ref behavior);
        sprite.Behavior = (WeaponBehavior)behavior;
        if (version == IntItemsVersion)
        {
            int count = reader.Read<int>();
            sprite.Items.AddRange(reader.Read<int>(count));
            return;
        }

        if (version > WordItemsVersion)
            return;

        int wordCount = reader.Read<int>();
        foreach (short item in reader.Read<short>(wordCount))
        {
            sprite.Items.Add(item);
        }
    }

    private void RestoreText(Sprite sprite, ref Resource reader, int version)
    {
        RestoreSprite(sprite, ref reader, version);
        var text = sprite.Text!;
        int behavior = (int)text.Behavior;
        reader.ReadInto(ref behavior);
        text.Behavior = (TextBehavior)behavior;
        SetText(sprite, reader.ReadText().ToArray());
    }
}
