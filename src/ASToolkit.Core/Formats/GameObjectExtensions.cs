namespace ASToolkit.Core.Formats;

internal static class GameObjectExtensions
{
    public static ObjectLayer GetLayer(this in GameObject obj, KnownObject id, VidResource? sprite)
    {
        var file = sprite?.Type ?? VidType.None;

        if (file.HasFlag(VidType.HardwareLayer))
            return ObjectLayer.Hardware;

        if (file.HasFlag(VidType.Light))
            return ObjectLayer.Light;

        if (file.HasFlag(VidType.Hardware))
            return GetHardwareLayer(id, obj, file, sprite!.Value.SurfaceCount);

        if (obj.SpriteClass == SpriteClass.BuiltTerrain)
            return file.HasFlag(VidType.Blended) ? ObjectLayer.BuiltAlpha : ObjectLayer.Built;

        if (obj.Properties.HasFlag(ObjectProperties.Ground))
            return ObjectLayer.SoftwareGround;

        if (obj.Properties.HasFlag(ObjectProperties.AlwaysTop))
            return ObjectLayer.SoftwareTop;

        if (file.HasFlag(VidType.Blended))
            return ObjectLayer.SoftwareAlpha;

        return (obj.Properties & (ObjectProperties.BuildSizeToGridZ | ObjectProperties.BuildVidZToGridZ)) == 0 ? ObjectLayer.Software : ObjectLayer.SoftwareGrid;
    }

    private static ObjectLayer GetHardwareLayer(KnownObject id, in GameObject obj, VidType file, int surfaces)
    {
        bool blended = file.HasFlag(VidType.Blended);
        bool depth = file.HasFlag(VidType.ZBuffer);

        if (obj.Properties.HasFlag(ObjectProperties.Ground))
            return ObjectLayer.Ground2;

        if (obj.SpriteType == SpriteType.Region)
            return ObjectLayer.Region;

        if (blended && depth)
            return ObjectLayer.Alpha;

        if (surfaces == 0)
            return ObjectLayer.Menu;

        if (depth)
            return ObjectLayer.Ground;

        if (id == KnownObject.Cursor)
            return ObjectLayer.Cursor;

        if (blended)
            return obj.Properties.HasFlag(ObjectProperties.Wave) ? ObjectLayer.AlphaGround : ObjectLayer.Alpha;

        return ObjectLayer.Hardware;
    }
}
