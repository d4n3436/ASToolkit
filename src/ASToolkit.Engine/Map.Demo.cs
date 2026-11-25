using System.Runtime.InteropServices;
using System.Text;
using ASToolkit.Engine.IO;
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
    /// Plays or records the input of the next demo frame, waiting until a played frame is due.
    /// </summary>
    /// <returns><see langword="true"/> to draw the frame; <see langword="false"/> when playback ended or the frame runs late.</returns>
    [EngineRoutine(0x0040e660)]
    public bool DemoTact()
    {
        bool shouldDraw = true;

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

            if ((uint)(time - Session.DemoPacedTime) > DemoMaxLag)
            {
                Session.DemoPacedTime = time - DemoMaxLag;
            }

            uint due = (uint)(time - Session.DemoPacedTime);
            if (Session.TimeGetTime() - Session.DemoPacedAt < due)
            {
                if (Session.TimeGetTime() - Session.DemoPacedAt <= due)
                {
                    uint realTime;
                    do
                    {
                        realTime = Session.TimeGetTime();
                    }
                    while (realTime - Session.DemoPacedAt <= due);
                }
            }
            else
            {
                shouldDraw = (uint)(CurrentTime - time) < DemoDrawLag;
            }

            SetTime(time);
            Session.DemoPacedTime = time;
            Session.DemoPacedAt = Session.TimeGetTime();
        }

        if (_recordedDemo is not null)
        {
            WriteDemo(_recordedDemo, new DemoFrame(CurrentTime, Input.Save()));
        }

        return shouldDraw;
    }

    /// <summary>
    /// Opens a demo file that the next map load records the input of every frame into.
    /// </summary>
    /// <param name="name">The path of the demo file.</param>
    /// <remarks>A file that is already open is closed first. When the file can't be created, nothing is recorded.</remarks>
    public void SaveDemo(ReadOnlySpan<byte> name)
    {
        CloseDemoFile();
        _demoFile = name.IsEmpty ? null : FileSystem.OpenWrite(Encoding.ASCII.GetString(name), false);
        if (_demoFile is null)
        {
            Session.Error.Error($"RES '{MyError.Decode(_demoName.Span)}' ''", LogError.Create, 0, name);
            return;
        }

        _demoName = name.ToArray();
    }

    /// <summary>
    /// Ends the recording that is running and returns the demo file it wrote.
    /// </summary>
    /// <returns>The bytes of the demo file.</returns>
    /// <exception cref="InvalidOperationException">No demo is being recorded.</exception>
    public byte[] FinishDemoRecording()
    {
        if (_recordedDemo is null)
            throw new InvalidOperationException("No demo is being recorded.");

        string path = Encoding.ASCII.GetString(_demoName.Span);
        EndDemoRecording();
        if (!FileSystem.TryRead(path, out var data))
            throw new InvalidOperationException($"Could not read the demo file '{path}'.");

        return data.ToArray();
    }

    private static void WriteDemo<T>(MemoryStream stream, T value)
        where T : unmanaged =>
        stream.Write(MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in value)));

    private void StartDemoRecording(ReadOnlySpan<byte> mapName, int seed)
    {
        _recordedDemo = new MemoryStream();
        _flags |= MapFlags.RecordingDemo;
        _recordedDemo.Write(mapName);
        _recordedDemo.WriteByte(0);
        WriteDemo(_recordedDemo, new DemoHeader(seed, CurrentTime));
    }

    private void CloseDemoFile()
    {
        if (_demoFile is null)
            return;

        _demoFile.Dispose();
        _demoFile = null;
        _demoName = "Not opened"u8.ToArray();
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
        Mouse.Enable(FileSystem);
        RequestLoad(mapName);
    }

    private void EndDemoRecording()
    {
        if (_recordedDemo is null)
            return;

        WriteDemo(_recordedDemo, DemoEndMarker);
        var container = new ResContainerBuilder { Magic = "RES "u8.ToArray(), Kind = "DEMO"u8.ToArray() };
        container.Chunks.Add(new ResChunkBuilder { Tag = "DEMO", Flags = ResChunkFlags.ExtendedHeader, Records = [_recordedDemo.ToArray()] });
        _demoFile!.Write(container.Build());
        _recordedDemo = null;
        CloseDemoFile();
    }
}
