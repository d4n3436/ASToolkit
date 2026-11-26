using System.Text;
using ASToolkit.Engine.IO;

namespace ASToolkit.Engine;

internal sealed class Movie
{
    private const int FileNotFound = unchecked((int)0x80070002);

    private static readonly int GraphBuilderResult = 0;
    private static readonly int MediaControlResult = 0;
    private static readonly int RenderFileResult = 0;

    private readonly GameSession _session;

    public Movie(GameSession session)
    {
        _session = session;
    }

    public bool IsOpen { get; private set; }

    private static ReadOnlySpan<byte> MovieLogTag => "MOVIE"u8;

    [EngineRoutine(0x00434480)]
    public void Open(ReadOnlySpan<byte> path)
    {
        Release();
        _session.Graphics.FlipToGdi();

        if (GraphBuilderResult < 0)
        {
            _session.Error.Error(MovieLogTag, LogError.Create, GraphBuilderResult, "GraphBuilder"u8);
            return;
        }

        if (MediaControlResult < 0)
        {
            _session.Error.Error(MovieLogTag, LogError.Create, MediaControlResult, "MediaControl"u8);
            Release();
            return;
        }

        int rendered = _session.Map!.FileSystem.Exists(Encoding.ASCII.GetString(path)) ? RenderFileResult : FileNotFound;
        if (rendered < 0)
        {
            _session.Error.Error(MovieLogTag, LogError.Invalid, rendered, "RenderFile"u8);
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

    [EngineRoutine(0x004346e0)]
    public void Pause()
    {
    }

    [EngineRoutine(0x004346f0)]
    public void Resume()
    {
    }
}
