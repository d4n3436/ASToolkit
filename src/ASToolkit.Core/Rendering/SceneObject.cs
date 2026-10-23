using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Rendering;

/// <summary>
/// Represents an object drawn in a map scene, either a placed sprite or an object the engine attaches to one.
/// </summary>
/// <param name="Id">The object ID in objects.res.</param>
/// <param name="Sprite">The placed sprite the object belongs to.</param>
/// <param name="X">The x position in map pixels.</param>
/// <param name="Y">The y position in map pixels.</param>
/// <param name="Z">The height above the floor.</param>
/// <param name="Facing">The facing the object is drawn with.</param>
public sealed record SceneObject(KnownObject Id, MergedMapSprite Sprite, float X, float Y, float Z, byte Facing);
