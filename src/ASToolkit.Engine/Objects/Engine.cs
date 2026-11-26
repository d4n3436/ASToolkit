using System.Numerics;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a train engine sprite.
/// </summary>
public sealed class Engine : Unit
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Engine"/> class on a map.
    /// </summary>
    /// <param name="vid">The shared data and frames of the sprite's object ID.</param>
    /// <param name="position">The position.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="parent">The sprite that the new sprite takes its army and height from, or null.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    [EngineRoutine(0x0044e8b0)]
    public Engine(Vid vid, Vector3 position, Angle direction, Sprite? parent, Map map)
        : base(vid, position, direction, parent, map)
    {
    }

    internal Engine? PrevEngine { get; set; }

    internal Engine? NextEngine { get; set; }

    internal bool IsDepoBatchEnd { get; set; }

    internal int DepoBatch { get; set; }

    [EngineRoutine(0x0043a920)]
    internal void SetCommandToTrain(Command command, int x, int y) => SetCommandToTrain(command, null, Map.RailMap.GetNearestDot(x, y), null);

    [EngineRoutine(0x00450000)]
    internal void SetCommandToTrain(Command command, Sprite? target, RailDot? first, RailDot? second)
    {
        // Stub
    }

    [EngineRoutine(0x00450580)]
    internal Engine? GetTrain()
    {
        // Stub
        return null;
    }

    [EngineRoutine(0x004505c0)]
    internal bool HaveArmy(Army army)
    {
        for (var car = this; car is not null; car = car.NextEngine)
        {
            if (car.Army == army)
                return true;
        }

        for (var car = PrevEngine; car is not null; car = car.PrevEngine)
        {
            if (car.Army == army)
                return true;
        }

        return false;
    }

    [EngineRoutine(0x004513c0)]
    internal void BreakTrain(float x, float y)
    {
        // Stub
    }

    [EngineRoutine(0x00451810)]
    internal Engine FirstEngine()
    {
        var first = this;
        while (first.PrevEngine is not null)
        {
            first = first.PrevEngine;
        }

        return first;
    }

    [EngineRoutine(0x00454530)]
    internal void CheckPrevNextEngine()
    {
        if (PrevEngine is not null && PrevEngine.NextEngine != this)
        {
            Map.Session.Error.Error($"ENGINE {(int)Vid.Id}", LogError.Invalid, 0, "PrevEngine->NextEngine!=this"u8);
            PrevEngine.NextEngine = this;
        }

        if (NextEngine is not null && NextEngine.PrevEngine != this)
        {
            Map.Session.Error.Error($"ENGINE {(int)Vid.Id}", LogError.Invalid, 0, "NextEngine->PrevEngine!=this"u8);
            NextEngine.PrevEngine = this;
        }
    }
}
