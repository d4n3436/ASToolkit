using System.Runtime.CompilerServices;
using ASToolkit.Bot;
using ASToolkit.Engine;

namespace ASToolkit.Tests.Engine;

internal static class MapSteppingExtensions
{
    private static readonly ConditionalWeakTable<Map, InputDriver> Drivers = [];
    private static readonly ConditionalWeakTable<Map, FramePacer> Pacers = [];

    public static void Play(this Map map, in PlayerCommand command)
    {
        Drivers.GetValue(map, created => new InputDriver(created)).Press(command);
        map.Step();
    }

    public static void Pace(this Map map, IReadOnlyList<int> frameTimes) => Pacers.AddOrUpdate(map, new FramePacer(frameTimes));

    public static FramePacer GetPacer(this Map map) => Pacers.GetValue(map, _ => new FramePacer(FramePacer.DisplayFrameTimes));

    public static bool Step(this Map map) => map.GetPacer().Step(map);

    public static bool StartPacedTact(this Map map) => map.GetPacer().StartTact(map);
}
