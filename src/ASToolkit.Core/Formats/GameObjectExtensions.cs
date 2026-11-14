namespace ASToolkit.Core.Formats;

internal static class GameObjectExtensions
{
    public static ObjectLayer GetLayer(this in GameObject obj, KnownObject id, VidResource? sprite)
    {
        var file = sprite?.Flags ?? VidFlags.None;

        if (file.HasFlag(VidFlags.SurfaceLayer))
            return ObjectLayer.Surface;

        if (file.HasFlag(VidFlags.OmniLight))
            return ObjectLayer.OmniLight;

        if (file.HasFlag(VidFlags.Surface))
            return GetSurfaceLayer(id, obj, file, sprite!.Value.SurfaceCount);

        if (obj.SpriteClass == SpriteClass.Scenery)
            return file.HasFlag(VidFlags.Blended) ? ObjectLayer.BlendedScenery : ObjectLayer.Scenery;

        if (obj.Flags.HasFlag(ObjectFlags.GroundDecal))
            return ObjectLayer.GroundDecal;

        if (obj.Flags.HasFlag(ObjectFlags.Raised))
            return ObjectLayer.Raised;

        if (file.HasFlag(VidFlags.Blended))
            return ObjectLayer.BlendedObject;

        return (obj.Flags & (ObjectFlags.Outline | ObjectFlags.Solid)) == 0 ? ObjectLayer.Object : ObjectLayer.SolidObject;
    }

    private static ObjectLayer GetSurfaceLayer(KnownObject id, in GameObject obj, VidFlags file, int surfaces)
    {
        bool blended = file.HasFlag(VidFlags.Blended);
        bool depth = file.HasFlag(VidFlags.Depth);

        if (obj.Flags.HasFlag(ObjectFlags.GroundDecal))
            return ObjectLayer.SurfaceDecal;

        if (obj.SpriteType == SpriteType.Railway)
            return ObjectLayer.RailwaySurface;

        if (blended && depth)
            return ObjectLayer.BlendedSurface;

        if (surfaces == 0)
            return ObjectLayer.EmptySurface;

        if (depth)
            return ObjectLayer.DepthSurface;

        if (id == KnownObject.Cursor)
            return ObjectLayer.Cursor;

        if (blended)
            return obj.Flags.HasFlag(ObjectFlags.Bobbing) ? ObjectLayer.BobbingSurface : ObjectLayer.BlendedSurface;

        return ObjectLayer.Surface;
    }
}
