using System.Text;
using ASToolkit.Engine.IO;

namespace ASToolkit.Engine;

public sealed partial class Graphics
{
    private const int FileNotFound = unchecked((int)0x80070002);
    private static ReadOnlySpan<byte> MovieLogTag => "MOVIE"u8;

    /// <summary>
    /// Gets a value indicating whether a movie is open for playback.
    /// </summary>
    /// <remarks>A movie stays open after it ends, until <see cref="StopMovie"/> is called.</remarks>
    public bool IsMovieOpen { get; private set; }

    [EngineRoutine(0x004343e0)]
    internal void PlayMovie(IGameFileSystem fileSystem, ReadOnlySpan<byte> path, GameLogger logger) => OpenMovie(fileSystem, path, logger);

    /// <summary>
    /// Stops the current movie.
    /// </summary>
    [EngineRoutine(0x00434680)]
    public void StopMovie() => IsMovieOpen = false;

    [EngineRoutine(0x00434480)]
    private void OpenMovie(IGameFileSystem fileSystem, ReadOnlySpan<byte> path, GameLogger logger)
    {
        StopMovie();

        if (!fileSystem.TryRead(Encoding.ASCII.GetString(path), out _))
        {
            logger.Error(MovieLogTag, LogError.Invalid, FileNotFound, "RenderFile"u8);
            StopMovie();
            return;
        }

        IsMovieOpen = true;
    }
}
