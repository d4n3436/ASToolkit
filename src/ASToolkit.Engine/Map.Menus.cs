using System.Numerics;
using ASToolkit.Core;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public partial class Map
{
    internal const int EndHandle = -1;

    internal static ReadOnlySpan<byte> MapLogTag => "MAP"u8;

    internal static ReadOnlySpan<byte> HeadTag => "HEAD"u8;

    internal static ReadOnlySpan<byte> SpriteTag => "SPR "u8;

    [EngineRoutine(0x0040ef30)]
    internal bool TryReadPointer(ref Resource reader, out Sprite? sprite)
    {
        sprite = null;

        if (!reader.TryRead(out int handle) || handle == EndHandle)
            return false;

        sprite = Relation.Decode(handle);

        return true;
    }

    [EngineRoutine(0x0040f0a0)]
    internal bool TryLoadSprite(ref Resource reader, int version, out Sprite? sprite)
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

        var vid = FindVid((KnownObject)id);
        if (vid is null)
        {
            Session.Error.Error(MapLogTag, LogError.Create, id, "sprite, this vid not exist"u8);
        }
        else
        {
            sprite = CreateSprite(vid, position, unchecked((byte)direction));
        }

        Relation.Insert(handle, sprite);

        sprite?.ChangeArmy(army);

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
        var vid = FindVid((KnownObject)id);
        if (vid is null)
        {
            Session.Error.Error(MapLogTag, LogError.Create, id, "sprite, this vid not exist"u8);
        }
        else
        {
            sprite = CreateSprite(vid, new Vector3(x, y, z), new Angle(direction));
        }

        Relation.Insert(handle, sprite);

        return true;
    }
}
