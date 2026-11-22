using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

internal readonly record struct SpritePart(KnownObject Id, GameObject Object, string Path, VidResource? File, int Frame, Vector3 Position, Vector3 Scale);
