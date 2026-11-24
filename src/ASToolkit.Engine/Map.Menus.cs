using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using ASToolkit.Core;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public sealed partial class Map
{
    private const int EndHandle = -1;
    private const int NoHandle = 0;
    private const int ItemsVersion = 12;
    private const int FogStepsPerUnit = 8;
    private const int FogRampShift = 3;
    private const int FloatRegionSizeVersion = 10;
    private const int WordItemsVersion = 10;
    private const int IntItemsVersion = 11;
    private const double MenuMatchTolerance = 0.001;
    private const int OldArmyVersion = 7;
    private const int OldStackActionSize = 12;
    private const int OldAttackAct = 0x27;
    private const int OldMoveAct = 0x28;
    private const int OldStopStackAct = 0x2f;

    private readonly List<(int Handle, Sprite? Sprite)> _spriteRelations = [];

    internal static ReadOnlySpan<byte> MapLogTag => "MAP"u8;

    private static ReadOnlySpan<byte> MenuKind => "MENU"u8;

    private static ReadOnlySpan<byte> HeadTag => "HEAD"u8;

    private static ReadOnlySpan<byte> SpriteTag => "SPR "u8;

    private static ReadOnlySpan<byte> PackedSpriteTag => "SPRI"u8;

    private static ReadOnlySpan<byte> MenuLogTag => "MENU"u8;

    [EngineRoutine(0x0040ef30)]
    internal bool TryReadRelatedSprite(ref Resource reader, out Sprite? sprite)
    {
        sprite = null;

        if (!reader.TryRead(out int handle) || handle == EndHandle)
            return false;

        sprite = GetRelatedSprite(handle);

        return true;
    }

    [EngineRoutine(0x00449d00)]
    private static void SetFogParameters(Region sprite, int near, int far, uint color, ReadOnlySpan<ushort> ramp, bool isPalettized)
    {
        sprite.FogFar = far;
        sprite.FogNear = near;
        sprite.FogColor = color;
        sprite.FogTable = default;

        if (near >= far)
            return;

        int span = far - near;
        ushort[] table = new ushort[span * FogStepsPerUnit + 1];

        for (int step = span * FogStepsPerUnit; step >= 0; step--)
        {
            int index = step * byte.MaxValue / span >> FogRampShift;
            table[step] = isPalettized ? (ushort)index : ramp[index];
        }

        sprite.FogTable = table;
    }

    private bool TryOpenMenu(ReadOnlySpan<byte> name, out Resource reader, out MenuHeader header)
    {
        reader = new Resource();
        header = default;
        if (reader.Open(_fileSystem, Session.Logger, name, MenuKind) != 0)
        {
            Session.Logger.Error(MenuLogTag, LogError.Open, 0, name);
            return false;
        }

        if (reader.FindChunk(HeadTag) != Resource.Found)
        {
            Session.Logger.Error(MenuLogTag, LogError.Section, 0, "'HEAD'in menu"u8);
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
        if (!TryOpenMenu(name, out var reader, out var header))
            return;

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
            var position = reader.Read<Vector3>();
            for (int i = 0; i < MenuList.Count; i++)
            {
                if (IsMenuElement(MenuList[i], header, id, position))
                {
                    DeleteMenuSpriteAt(i);
                    i--;
                }
            }

            reader.NextRecord(SpriteTag);
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

        return (int)sprite.Definition.Id == id &&
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
            Session.Logger.Error($"SPRITE {(int)sprite.Definition.Id}", LogError.Invalid, count, "noRef at Release"u8);

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

        var definition = Definitions.Find((KnownObject)id);
        if (definition is null)
        {
            Session.Logger.Error(MapLogTag, LogError.Create, id, "sprite, this vid not exist"u8);
        }
        else
        {
            sprite = AddSprite(definition, position, unchecked((byte)direction));
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
        var definition = Definitions.Find((KnownObject)id);
        if (definition is null)
        {
            Session.Logger.Error(MapLogTag, LogError.Create, id, "sprite, this vid not exist"u8);
        }
        else
        {
            sprite = AddSprite(definition, new Vector3(x, y, z), new Angle(direction));
        }

        if (handle != NoHandle)
        {
            _spriteRelations.Add((handle, sprite));
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
        switch (sprite)
        {
            case Unit unit when unit is not (CivRobot or Depo or Engine):
                RestoreUnit(unit, ref reader, version);
                break;
            case Region region:
                RestoreRegion(region, ActionCode.Restore, ref reader, version);
                break;
            case Text text:
                RestoreText(text, ref reader, version);
                break;
            default:
                RestoreSprite(sprite, ref reader, version);
                break;
        }
    }

    private void RestoreOld(Sprite sprite, ref Resource reader, int version)
    {
        switch (sprite)
        {
            case Unit unit when unit is not (CivRobot or Depo or Engine):
                RestoreOldUnit(unit, ref reader, version);
                break;
            case Terrain and not (Rail or Unit):
                RestoreOldTerrain(sprite, ref reader, version);
                break;
            case Region region:
                RestoreRegion(region, ActionCode.RestoreOld, ref reader, version);
                break;
            default:
                RestoreOldSprite(sprite, ref reader, version);
                break;
        }
    }

    private void RestoreOldUnit(Unit sprite, ref Resource reader, int version)
    {
        RestoreOldTerrain(sprite, ref reader, version);
        if (version < OldArmyVersion)
        {
            byte army = 0;
            reader.ReadInto(ref army);
            sprite.ChangeArmy(army);
        }

        byte behavior = (byte)sprite.Behavior;
        reader.ReadInto(ref behavior);
        sprite.Behavior = (WeaponBehavior)((int)sprite.Behavior & ~byte.MaxValue | behavior);
    }

    private void RestoreOldTerrain(Sprite sprite, ref Resource reader, int version)
    {
        RestoreOldSprite(sprite, ref reader, version);
        Span<byte> skipped = stackalloc byte[sizeof(short)];
        reader.Read(skipped[..(version > OldArmyVersion ? sizeof(short) : sizeof(byte))]);
    }

    private void RestoreOldSprite(Sprite sprite, ref Resource reader, int version)
    {
        short read = 0;
        reader.ReadInto(ref read);
        int count = Math.Max((int)read, 0);
        var buffer = sprite.ActionStack.Buffer;
        var entries = new StackAction[Math.Max(buffer.Length, count)];
        for (int i = 0; i < buffer.Length; i++)
        {
            ref readonly var kept = ref buffer[i];
            entries[i] = new StackAction(kept.Value, kept.Var1, kept.Var2, kept.Var3);
        }

        reader.Read(MemoryMarshal.AsBytes(entries.AsSpan())[..(count * OldStackActionSize)]);
        if (version < OldArmyVersion)
        {
            count = 0;
        }

        for (int i = 0; i < count; i++)
        {
            ref readonly var entry = ref entries[i];
            int act = entry.Act & byte.MaxValue;
            switch (act)
            {
                case OldMoveAct:
                    act = (int)ActionCode.Move;
                    break;
                case OldAttackAct:
                    act = (int)ActionCode.Attack;
                    break;
                case OldStopStackAct:
                    act = (int)ActionCode.StopStack;
                    break;
                default:
                    Session.Logger.Error($"SPRITE {(int)sprite.Definition.Id}", LogError.Unknown, act, "actionStack.act restore"u8);
                    break;
            }

            entries[i] = new StackAction(act, entry.Var1, entry.Var2, 0);
        }

        LoadOrders(sprite, entries.AsSpan(0, count));
        if (version < OldArmyVersion)
            return;

        byte army = 0;
        reader.ReadInto(ref army);
        sprite.ChangeArmy(army);
    }

    private void RestoreSprite(Sprite sprite, ref Resource reader, int version)
    {
        int count = sprite.ActionStack.Count;
        reader.ReadInto(ref count);
        LoadOrders(sprite, reader.Read<StackAction>(count));

        if (version < ItemsVersion)
            return;

        int itemCount = 0;
        reader.ReadInto(ref itemCount);
        var items = reader.Read<int>(itemCount);
        if (itemCount != 0)
        {
            sprite.Items.Clear();
            sprite.Items.AddRange(items);
        }
    }

    [EngineRoutine(0x00449e20)]
    private void RestoreRegion(Region sprite, ActionCode code, ref Resource reader, int version)
    {
        if (code == ActionCode.Restore)
        {
            RestoreSprite(sprite, ref reader, version);
        }
        else
        {
            RestoreOldSprite(sprite, ref reader, version);
        }

        var fog = default(RegionFog);
        reader.ReadInto(ref fog);
        sprite.Flags = fog.Flags;
        SetFogParameters(sprite, fog.Near, fog.Far, fog.Color, Session.Graphics.ColorTable, (Session.Graphics.Screen?.TextureFormat ?? Session.Graphics.Buffers.LightFormat) == VidPixelFormat.P8);
        if (version < FloatRegionSizeVersion)
        {
            var size = default(Size);
            reader.ReadInto(ref size);
            sprite.Size = new Vector2(size.Width, size.Height);
        }
        else
        {
            var size = default(Vector2);
            reader.ReadInto(ref size);
            sprite.Size = size;
        }

        sprite.UnusedValue = reader.Read<int>();
        sprite.UnusedDefinition = Definitions.Find((KnownObject)reader.Read<int>()) ?? Definitions.Empty;
        for (int pair = 0; pair < Region.Pairs; pair++)
        {
            var replacement = new RegionReplacement(-1, -1);
            reader.ReadInto(ref replacement);
            var fromDefinition = Definitions.Find((KnownObject)replacement.From);
            sprite.From[pair] = fromDefinition;
            sprite.To[pair] = Definitions.Find((KnownObject)replacement.To);
            fromDefinition?.IsRegionReplaced = true;
        }
    }

    private void RestoreUnit(Unit sprite, ref Resource reader, int version)
    {
        RestoreSprite(sprite, ref reader, version);
        int behavior = (int)sprite.Behavior;
        reader.ReadInto(ref behavior);
        sprite.Behavior = (WeaponBehavior)behavior;
        if (version == IntItemsVersion)
        {
            int count = 0;
            reader.ReadInto(ref count);
            sprite.Items.AddRange(reader.Read<int>(count));
            return;
        }

        if (version > WordItemsVersion)
            return;

        int wordCount = 0;
        reader.ReadInto(ref wordCount);
        foreach (short item in reader.Read<short>(wordCount))
        {
            sprite.Items.Add(item);
        }
    }

    private void RestoreText(Text text, ref Resource reader, int version)
    {
        RestoreSprite(text, ref reader, version);
        int behavior = (int)text.TextBehavior;
        reader.ReadInto(ref behavior);
        text.TextBehavior = (TextBehavior)behavior;
        SetText(text, reader.ReadText().ToArray());
    }
}
