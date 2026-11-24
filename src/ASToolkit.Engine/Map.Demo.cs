using System.Runtime.InteropServices;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public partial class Map
{
    private const int DemoMaxLag = 71;
    private const int DemoDrawLag = 21;
    private const int DemoEndMarker = -1;

    /// <summary>
    /// Gets a value indicating whether a demo drives the input.
    /// </summary>
    public bool IsPlayingDemo => _flags.HasFlag(MapFlags.PlayingDemo);

    /// <summary>
    /// Gets the demo being played.
    /// </summary>
    public DemoResource? PlayedDemo => _playedDemo;

    /// <summary>
    /// Gets the offset of the next frame in the played demo.
    /// </summary>
    public int PlayedDemoOffset => _playedOffset;

    /// <summary>
    /// Plays or records the input of the next demo frame.
    /// </summary>
    /// <param name="now">The real time in milliseconds.</param>
    /// <param name="waitUntil">The real time to wait for before the frame, or <paramref name="now"/> when the frame is due.</param>
    /// <returns><see langword="true"/> to draw the frame; <see langword="false"/> when playback ended or the frame runs late.</returns>
    [EngineRoutine(0x0040e660)]
    public bool StepDemo(uint now, out uint waitUntil)
    {
        bool shouldDraw = true;
        waitUntil = now;

        if (_playedDemo is not null)
        {
            if (!_playedDemo.Value.TryReadFrame(ref _playedOffset, out var frame) || Input.Key != 0 ||
                Input.Flags.HasFlag(InputFlags.LeftClick) || Input.Flags.HasFlag(InputFlags.RightClick))
            {
                EndDemoPlayback();

                return false;
            }

            Input.Load(frame.Input);
            int time = frame.Time;

            if ((uint)(time - _demoPacedTime) > DemoMaxLag)
            {
                _demoPacedTime = time - DemoMaxLag;
            }

            uint due = (uint)(time - _demoPacedTime);

            if (now - _demoPacedAt < due)
            {
                waitUntil = _demoPacedAt + due + 1;
            }
            else
            {
                shouldDraw = (uint)(Time - time) < DemoDrawLag;
            }

            SetTime(time);
            _demoPacedTime = time;
            _demoPacedAt = waitUntil;
        }

        if (_recordedDemo is not null)
        {
            WriteDemo(_recordedDemo, new DemoFrame(Time, Input.Save()));
        }

        return shouldDraw;
    }

    /// <summary>
    /// Ends the recording if it is still running.
    /// </summary>
    /// <returns>The bytes of the demo file.</returns>
    /// <exception cref="InvalidOperationException">No demo has been recorded.</exception>
    public byte[] FinishDemoRecording()
    {
        EndDemoRecording();

        var body = _endedDemo ?? throw new InvalidOperationException("No demo has been recorded.");
        var container = new ResContainerBuilder { Magic = "RES "u8.ToArray(), Kind = "DEMO"u8.ToArray() };
        container.Chunks.Add(new ResChunkBuilder { Tag = "DEMO", Flags = ResChunkFlags.ExtendedHeader, Records = [body.ToArray()] });

        return container.Build();
    }

    private static void WriteDemo<T>(MemoryStream stream, T value)
        where T : unmanaged =>
        stream.Write(MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in value)));

    private void StartDemoRecording(ReadOnlySpan<byte> mapName, int seed)
    {
        _endedDemo = null;
        _recordedDemo = new MemoryStream();
        _flags |= MapFlags.RecordingDemo;
        _recordedDemo.Write(mapName);
        _recordedDemo.WriteByte(0);
        WriteDemo(_recordedDemo, new DemoHeader(seed, Time));
    }

    private void EndDemoPlayback()
    {
        var mapName = _playedDemo!.Value.MapName;

        if (mapName.IsEmpty)
        {
            Input.PostMessage(WindowMessageId.Close, 0, 0);

            return;
        }

        _playedDemo = null;
        _flags &= ~MapFlags.PlayingDemo;
        CursorSprite.Enable(FileSystem);
        RequestLoad(mapName);
    }

    private void EndDemoRecording()
    {
        if (_recordedDemo is null)
            return;

        WriteDemo(_recordedDemo, DemoEndMarker);
        _endedDemo = _recordedDemo;
        _recordedDemo = null;
    }
}
