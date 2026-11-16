using System.Buffers.Binary;
using System.Runtime.InteropServices;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal sealed partial class GameWorld
{
    private const int DemoMaxLag = 71;
    private const int DemoDrawLag = 21;
    private const int DemoEndMarker = -1;
    private DemoResource? _playedDemo;
    private int _playedFrame;
    private int _demoPacedTime;
    private uint _demoPacedAt;
    private MemoryStream? _recordedDemo;

    public bool IsPlayingDemo => _playedDemo is not null;

    public bool IsRecordingDemo => _recordedDemo is not null;

    public byte[]? MapToLoad { get; private set; }

    public bool IsQuitting { get; private set; }

    [EngineRoutine(0x0040c960)]
    public void StartDemoPlayback(DemoResource demo)
    {
        _playedDemo = demo;
        _playedFrame = 0;
        SetTime(demo.StartTime);
    }

    [EngineRoutine(0x0040c960)]
    public void StartDemoRecording(ReadOnlySpan<byte> mapName, uint seed, ReadOnlySpan<byte> scriptState)
    {
        _recordedDemo = new MemoryStream();
        _recordedDemo.Write(mapName);
        _recordedDemo.WriteByte(0);
        Span<byte> header = stackalloc byte[sizeof(uint) + sizeof(int)];
        BinaryPrimitives.WriteUInt32LittleEndian(header, seed);
        BinaryPrimitives.WriteInt32LittleEndian(header[sizeof(uint)..], Time);
        _recordedDemo.Write(header);
        _recordedDemo.Write(scriptState);
    }

    // Returns whether to draw the frame, always after waiting for real time to catch up
    [EngineRoutine(0x0040e660)]
    public bool StepDemo(uint now, out uint waitUntil)
    {
        bool shouldDraw = true;
        waitUntil = now;

        if (_playedDemo is not null)
        {
            var frames = _playedDemo.Value.Frames;

            if (_playedFrame >= frames.Length || Input.Key != 0 || (Input.Flags & (InputFlags.LeftClick | InputFlags.RightClick)) != 0)
            {
                EndDemoPlayback();

                return false;
            }

            ref readonly var frame = ref frames[_playedFrame++];
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
            var frame = new DemoFrame(Time, Input.Save());
            _recordedDemo.Write(MemoryMarshal.AsBytes(new ReadOnlySpan<DemoFrame>(in frame)));
        }

        return shouldDraw;
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

        MapToLoad = mapName.ToArray();
    }

    [EngineRoutine(0x0040c570)]
    public byte[] FinishDemoRecording()
    {
        var body = _recordedDemo!;
        _recordedDemo = null;
        Span<byte> marker = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(marker, DemoEndMarker);
        body.Write(marker);
        var container = new ResContainerBuilder { Magic = "RES "u8.ToArray(), Kind = "DEMO"u8.ToArray() };
        container.Chunks.Add(new ResChunkBuilder { Tag = "DEMO", Flags = ResChunkFlags.ExtendedHeader, Records = [body.ToArray()] });

        return container.Build();
    }
}
