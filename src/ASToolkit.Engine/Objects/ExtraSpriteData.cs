using System.Numerics;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

internal sealed class ExtraSpriteData
{
    [EngineRoutine(0x0043f1a0)]
    public ExtraSpriteData(Sprite sprite)
    {
        StreamPeriod = 1;
        Lifetime = sprite.Vid.Lifetime;
        KeyframeStart = sprite.Map.CurrentTime;
        MaxSpeed = sprite.Vid.Speed;
        PreviousPosition = sprite.Position;
    }

    public Vector3 PreviousPosition { get; set; }

    public uint PreviousPositionRealTime { get; set; }

    public int Lifetime { get; set; }

    public int StreamPeriod { get; set; }

    public int KeyframeStart { get; set; }

    public float KeyframePosition { get; set; }

    public float MaxSpeed { get; set; }

    public Gamma GammaOverride { get; set; }

    public MyList<int> Items { get; } = new();
}
