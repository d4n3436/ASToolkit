using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal readonly record struct SpritePart(KnownObject Id, GameObject Object, string Path, VidResource? File, int Frame, Vector3 Position, Vector3 Scale);
