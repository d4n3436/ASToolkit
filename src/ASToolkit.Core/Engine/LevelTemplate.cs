using System.Numerics;
using ASToolkit.Core.Engine.Scripts;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents the model, definitions, terrain and script source a world loads a level from.
/// </summary>
public sealed class LevelTemplate
{
    /// <summary>
    /// Gets the model of the level.
    /// </summary>
    public required LevelModel Model { get; init; }

    /// <summary>
    /// Gets the sprite definitions of the level.
    /// </summary>
    public required SpriteCatalog Definitions { get; init; }

    /// <summary>
    /// Gets the terrain heights of the level.
    /// </summary>
    public required MovementMap Movement { get; init; }

    /// <summary>
    /// Gets the walkable bounds of the level.
    /// </summary>
    public required WorldBounds Bounds { get; init; }

    /// <summary>
    /// Gets the rounds an ammunition pickup gives per weapon slot.
    /// </summary>
    public required IReadOnlyList<int> AmmoBatches { get; init; }

    /// <summary>
    /// Gets the frame lengths the clock cycles through.
    /// </summary>
    public required IReadOnlyList<int> FrameTimes { get; init; }

    /// <summary>
    /// Gets the player's start when the map places none.
    /// </summary>
    public Vector2 FallbackStart { get; init; }

    /// <summary>
    /// Gets the script files the level script compiles from.
    /// </summary>
    public required IScriptFileSource ScriptFiles { get; init; }

    /// <summary>
    /// Gets the path of the level script.
    /// </summary>
    public required string ScriptPath { get; init; }

    /// <summary>
    /// Gets the indices of the stored sprites before which a recording shows the game redrawing its loading screen, with the stored-sprite count for a redraw after the last.
    /// </summary>
    public IReadOnlyList<int> LoadingScreenRecords { get; init; } = [];
}
