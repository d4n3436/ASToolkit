using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Routes;

// A placed object or one of the objects linked to it, where the engine puts it
internal readonly record struct SpritePart(KnownObject Id, GameObject Object, string Path, VidResource? File, int Frame, Vector3 Position, Vector3 Scale);
