using System.Text;
using ASToolkit.Engine.IO;

namespace ASToolkit.Engine;

internal sealed class Movie
{
    private const int FileNotFound = unchecked((int)0x80070002);

    private static readonly int GraphBuilderResult = 0;
    private static readonly int MediaControlResult = 0;
    private static readonly int RenderFileResult = 0;

    private readonly MyError _error;

    public Movie(MyError error)
    {
        _error = error;
    }

    public bool IsOpen { get; private set; }

    private static ReadOnlySpan<byte> MovieLogTag => "MOVIE"u8;

    [EngineRoutine(0x00434480)]
    public void Open(IGameFileSystem fileSystem, ReadOnlySpan<byte> path)
    {
        Release();

        if (GraphBuilderResult < 0)
        {
            _error.Error(MovieLogTag, LogError.Create, GraphBuilderResult, "GraphBuilder"u8);
            return;
        }

        if (MediaControlResult < 0)
        {
            _error.Error(MovieLogTag, LogError.Create, MediaControlResult, "MediaControl"u8);
            Release();
            return;
        }

        int rendered = fileSystem.TryRead(Encoding.ASCII.GetString(path), out _) ? RenderFileResult : FileNotFound;
        if (rendered < 0)
        {
            _error.Error(MovieLogTag, LogError.Invalid, rendered, "RenderFile"u8);
            Release();
            return;
        }

        IsOpen = true;
    }

    [EngineRoutine(0x00434650)]
    public bool Update() => true;

    [EngineRoutine(0x00434680)]
    public void Release()
    {
        IsOpen = false;
    }
}
