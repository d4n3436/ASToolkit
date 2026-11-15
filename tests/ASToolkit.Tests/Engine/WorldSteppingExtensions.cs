using System.Runtime.CompilerServices;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Simulation;

namespace ASToolkit.Tests.Engine;

internal static class WorldSteppingExtensions
{
    private static readonly ConditionalWeakTable<GameWorld, InputDriver> Drivers = [];

    public static void Step(this GameWorld world, in PlayerCommand command)
    {
        Drivers.GetValue(world, created => new InputDriver(created)).Press(command);
        world.Step();
        world.AdvanceClock();
    }
}
