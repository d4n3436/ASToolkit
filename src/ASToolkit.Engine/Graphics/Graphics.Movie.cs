using System.Text;
using ASToolkit.Engine.IO;

namespace ASToolkit.Engine;

public sealed partial class Graphics
{
    private const int FileNotFound = unchecked((int)0x80070002);

    private static readonly int GraphBuilderResult = 0;
    private static readonly int MediaControlResult = 0;
    private static readonly int RenderFileResult = 0;

    /// <summary>
    /// Gets a value indicating whether a movie is open for playback.
    /// </summary>
    /// <remarks>A movie stays open after it ends, until <see cref="StopMovie"/> is called.</remarks>
    public bool IsMovieOpen { get; private set; }

    private static ReadOnlySpan<byte> MovieLogTag => "MOVIE"u8;

    /// <summary>
    /// Stops the current movie.
    /// </summary>
    [EngineRoutine(0x00434680)]
    public void StopMovie()
    {
        IsMovieOpen = false;
    }

    [EngineRoutine(0x00434650)]
    internal bool HasMovieEnded() => true;

    [EngineRoutine(0x004343e0)]
    internal void PlayMovie(IGameFileSystem fileSystem, ReadOnlySpan<byte> path, MyError error) => OpenMovie(fileSystem, path, error);

    [EngineRoutine(0x00434480)]
    private void OpenMovie(IGameFileSystem fileSystem, ReadOnlySpan<byte> path, MyError error)
    {
        StopMovie();

        if (GraphBuilderResult < 0)
        {
            error.Error(MovieLogTag, LogError.Create, GraphBuilderResult, "GraphBuilder"u8);
            return;
        }

        if (MediaControlResult < 0)
        {
            error.Error(MovieLogTag, LogError.Create, MediaControlResult, "MediaControl"u8);
            StopMovie();
            return;
        }

        int rendered = fileSystem.TryRead(Encoding.ASCII.GetString(path), out _) ? RenderFileResult : FileNotFound;
        if (rendered < 0)
        {
            error.Error(MovieLogTag, LogError.Invalid, rendered, "RenderFile"u8);
            StopMovie();
            return;
        }

        IsMovieOpen = true;
    }
}
