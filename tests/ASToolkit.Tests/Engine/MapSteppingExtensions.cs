using System.Runtime.CompilerServices;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Simulation;

namespace ASToolkit.Tests.Engine;

internal static class MapSteppingExtensions
{
    private static readonly ConditionalWeakTable<Map, InputDriver> Drivers = [];

    public static void Play(this Map map, in PlayerCommand command)
    {
        Drivers.GetValue(map, created => new InputDriver(created)).Press(command);
        map.Step();
        map.AdvanceClock();
    }
}
