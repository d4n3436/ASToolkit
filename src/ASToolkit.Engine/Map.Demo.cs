using System.Buffers.Binary;
using System.Runtime.InteropServices;
using ASToolkit.Formats;
using ASToolkit.Core;

namespace ASToolkit.Engine;

public sealed partial class Map
{
    private const int DemoMaxLag = 71;
    private const int DemoDrawLag = 21;
    private const int DemoEndMarker = -1;

    private DemoResource? _playedDemo;
    private int _playedOffset;
    private int _demoPacedTime;
    private uint _demoPacedAt;
    private MemoryStream? _recordedDemo;
    private MemoryStream? _endedDemo;

    /// <summary>
    /// Gets a value indicating whether a demo drives the input.
    /// </summary>
    public bool IsPlayingDemo => _playedDemo is not null;

    /// <summary>
    /// Gets the name of the map the level script requested next, or null if the script didn't request one.
    /// </summary>
    public byte[]? MapToLoad { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the game is quitting.
    /// </summary>
    public bool IsQuitting { get; private set; }

    /// <summary>
    /// Attempts to get the game time of the next frame of the played demo.
    /// </summary>
    /// <param name="time">The game time of the next frame in milliseconds when the demo has one; otherwise, 0.</param>
    /// <returns><see langword="true"/> when the demo has a next frame; otherwise, <see langword="false"/>.</returns>
    public bool TryGetNextDemoTime(out int time)
    {
        int offset = _playedOffset;
        time = 0;

        if (_playedDemo is null || !_playedDemo.Value.TryReadFrame(ref offset, out var frame))
            return false;

        time = frame.Time;

        return true;
    }

    /// <summary>
    /// Returns the game time of every frame the played demo has left.
    /// </summary>
    /// <returns>The game times in milliseconds, in frame order.</returns>
    public IEnumerable<int> EnumerateDemoFrameTimes()
    {
        if (_playedDemo is null)
            yield break;

        var demo = _playedDemo.Value;
        int offset = _playedOffset;
        while (demo.TryReadFrame(ref offset, out var frame))
        {
            yield return frame.Time;
        }
    }

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
        _recordedDemo.Write(mapName);
        _recordedDemo.WriteByte(0);
        WriteDemo(_recordedDemo, new DemoHeader(seed, Time));
        Script.WriteState(_recordedDemo);
    }

    private void EndDemoPlayback()
    {
        var mapName = _playedDemo!.Value.MapName;
        _playedDemo = null;

        if (mapName.IsEmpty)
        {
            IsQuitting = true;

            return;
        }

        CursorSprite.LoadCursor(_fileSystem);
        MapToLoad = mapName.ToArray();
    }

    private void EndDemoRecording()
    {
        if (_recordedDemo is null)
            return;

        Span<byte> marker = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(marker, DemoEndMarker);
        _recordedDemo.Write(marker);
        _endedDemo = _recordedDemo;
        _recordedDemo = null;
    }
}
