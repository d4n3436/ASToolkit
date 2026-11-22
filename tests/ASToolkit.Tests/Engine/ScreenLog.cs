using ASToolkit.Engine;
using ASToolkit.Engine.Scripts;
using ASToolkit.Formats;

namespace ASToolkit.Tests.Engine;

internal sealed class ScreenLog
{
    public List<int> Shakes { get; } = [];

    public List<(uint Started, int Color)> Flashes { get; } = [];

    public void Read(Map map)
    {
        var graphics = map.Session.Graphics;
        if (graphics.Environment.HasFlag(MapEnvironment.Earthquake))
        {
            Shakes.Add(map.Time);
        }

        uint started = graphics.EffectStartTimes[(int)ScreenEffect.Nuke];
        if (started != 0 && (Flashes.Count == 0 || Flashes[^1].Started != started))
        {
            Flashes.Add((started, graphics.EffectFirst[(int)ScreenEffect.Nuke]));
        }
    }
}
