using ASToolkit.Engine.IO;

namespace ASToolkit.Engine;

public sealed partial class Graphics
{
    internal Movie Movie { get; } = new();

    [EngineRoutine(0x004343e0)]
    internal void PlayMovie(IGameFileSystem fileSystem, ReadOnlySpan<byte> path, MyError error) => Movie.Open(fileSystem, path, error);
}
