using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Rendering;

/// <summary>
/// Represents an object drawn in a map scene, either a placed sprite or an object the engine attaches to one.
/// </summary>
/// <param name="Id">The object ID in objects.res.</param>
/// <param name="Sprite">The placed sprite the object belongs to.</param>
/// <param name="Position">The position in map pixels, with the height above the floor.</param>
/// <param name="Direction">The direction the object is drawn with.</param>
public sealed record SceneObject(KnownObject Id, MergedMapSprite Sprite, Vector3 Position, Angle Direction);
