using System.Runtime.CompilerServices;
using ASToolkit.Bot;
using ASToolkit.Engine;

namespace ASToolkit.Tests.Engine;

internal static class MapSteppingExtensions
{
    private static readonly ConditionalWeakTable<Map, InputDriver> Drivers = [];
    private static readonly ConditionalWeakTable<Map, FramePacer> Pacers = [];

    extension(Map map)
    {
        public void Play(in PlayerCommand command)
        {
            Drivers.GetValue(map, created => new InputDriver(created, created.Session.Controls)).Press(command);
            map.Step();
        }

        public void Pace(IReadOnlyList<int> frameTimes) => Pacers.AddOrUpdate(map, new FramePacer(frameTimes));

        public FramePacer GetPacer() => Pacers.GetValue(map, _ => new FramePacer(FramePacer.DisplayFrameTimes));

        public bool Step() => map.GetPacer().Step(map);

        public bool StartPacedTact() => map.GetPacer().StartTact(map);
    }
}
