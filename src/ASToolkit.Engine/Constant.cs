using System.Numerics;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the game's tuning values from the object resource, in the units the engine works in.
/// </summary>
public sealed class Constant
{
    [EngineRoutine(0x0042df40)]
    internal Constant(ref Resource resource, MyError? error)
    {
        if (resource.GoBegin("CNST"u8) != Resource.Found)
        {
            error?.Log("!!!ERROR!!! CNST Load Constant section not found"u8);
            return;
        }

        var record = resource.Read<ObjectConstants>();
        ScrollSpeed = record.ScrollSpeed * Vid.PerMillisecond;
        Gravitation = record.Gravitation * Vid.PerMillisecondSquared;
        Gravitation2 = record.Gravitation2 * Vid.PerMillisecondSquared;
        HpPerSecond = record.HpPerSecond;
        HoverDelay = record.HoverDelay;
        SelectedGamma = record.SelectedGamma;
        HoveredEnemyGamma = record.HoveredEnemyGamma;
        HoveredGamma = record.HoveredGamma;
    }

    /// <summary>
    /// Gets the fastest scroll speed of the view along each axis in pixels per millisecond.
    /// </summary>
    public Vector2 ScrollSpeed { get; }

    /// <summary>
    /// Gets the gravity of falling objects in pixels per millisecond squared.
    /// </summary>
    public float Gravitation { get; }

    /// <summary>
    /// Gets the second gravity in pixels per millisecond squared.
    /// </summary>
    public float Gravitation2 { get; }

    /// <summary>
    /// Gets the time in milliseconds the cursor must rest before a mouse tip shows.
    /// </summary>
    public int HoverDelay { get; }

    /// <summary>
    /// Gets the hit points that sprites of object ID 104 heal each second.
    /// </summary>
    public int HpPerSecond { get; }

    /// <summary>
    /// Gets the packed gamma applied to the player's sprite, or zero if there isn't one.
    /// </summary>
    public uint SelectedGamma { get; }

    /// <summary>
    /// Gets the packed gamma applied to an enemy under the cursor, or zero if there isn't one.
    /// </summary>
    public uint HoveredEnemyGamma { get; }

    /// <summary>
    /// Gets the packed gamma applied to a sprite under the cursor, or zero if there isn't one.
    /// </summary>
    public uint HoveredGamma { get; }

    /// <summary>
    /// Gets a value indicating whether the debug mode is on.
    /// </summary>
    public bool IsDebugMode { get; internal set; }
}
