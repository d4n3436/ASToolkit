using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents one of the objects a placed sprite puts on the map, the sprite itself or an object linked to it.
/// </summary>
/// <param name="Id">The object ID.</param>
/// <param name="Object">The definition of the object in the object table.</param>
/// <param name="Path">The path of the sprite file of the object.</param>
/// <param name="File">The sprite file of the object, or null when the file can't be read or holds no frames.</param>
/// <param name="Frame">The frame of the sprite file the object shows.</param>
/// <param name="Position">The position of the object.</param>
/// <param name="Scale">The scale of the object along x, y and the height.</param>
public readonly record struct SpritePart(KnownObject Id, GameObject Object, string Path, VidResource? File, int Frame, Vector3 Position, Vector3 Scale);
