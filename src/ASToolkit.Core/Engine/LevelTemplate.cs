using ASToolkit.Core.Engine.IO;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents the map contents, definitions, terrain and script source that a map loads a level from.
/// </summary>
public sealed record LevelTemplate
{
    private const string ScriptExtension = ".lgc";
    private const string MapExtension = ".map";

    /// <summary>
    /// Gets the name of the map as level scripts see it.
    /// </summary>
    public string MapName
    {
        get => field ?? Path.ChangeExtension(ScriptPath, MapExtension);
        init;
    }

    /// <summary>
    /// Gets the contents of the map file.
    /// </summary>
    public required MapData MapData { get; init; }

    /// <summary>
    /// Gets the sprite definitions of the level.
    /// </summary>
    public required SpriteCatalog Definitions { get; init; }

    /// <summary>
    /// Gets the terrain heights of the level.
    /// </summary>
    public required MovementMap Movement { get; init; }

    /// <summary>
    /// Gets the rounds an ammunition pickup gives per weapon slot.
    /// </summary>
    public required IReadOnlyList<int> AmmoBatches { get; init; }

    /// <summary>
    /// Gets the frame lengths the clock cycles through.
    /// </summary>
    public required IReadOnlyList<int> FrameTimes { get; init; }

    /// <summary>
    /// Gets the file system the level script and its includes are read from.
    /// </summary>
    public required IGameFileSystem FileSystem { get; init; }

    /// <summary>
    /// Gets the path of the level script.
    /// </summary>
    public required string ScriptPath { get; init; }

    /// <summary>
    /// Gets the indexes of the stored sprites before which a recording shows the game redrawing its loading screen, where an index equal to the number of stored sprites marks a redraw after the last one.
    /// </summary>
    public IReadOnlyList<int> LoadingScreenRecords { get; init; } = [];

    /// <summary>
    /// Creates a template from a map file and the level script beside it.
    /// </summary>
    /// <param name="fileSystem">The file system the map, the level script and its includes are read from.</param>
    /// <param name="mapPath">The path of the map, relative to the game folder.</param>
    /// <param name="definitions">The sprite definitions.</param>
    /// <param name="ammoBatches">The rounds an ammunition pickup gives per weapon slot.</param>
    /// <param name="mapName">The name of the map as level scripts see it, in the form a script passes to <c>Load</c>, or null to use <paramref name="mapPath"/>.</param>
    /// <returns>The template.</returns>
    /// <exception cref="FileNotFoundException">The map file does not exist.</exception>
    public static LevelTemplate FromMap(IGameFileSystem fileSystem, string mapPath, SpriteCatalog definitions, IReadOnlyList<int> ammoBatches, string? mapName = null)
    {
        if (!fileSystem.TryRead(mapPath, out var data))
        {
            throw new FileNotFoundException($"Could not find map file '{mapPath}'.", mapPath);
        }

        return FromMapData(MapData.FromMap(MapResourceBuilder.Parse(data)), fileSystem, Path.ChangeExtension(mapPath, ScriptExtension), definitions, ammoBatches) with { MapName = mapName ?? mapPath };
    }

    /// <summary>
    /// Creates a template from a level model and the path of its level script.
    /// </summary>
    /// <param name="model">The model of the level.</param>
    /// <param name="fileSystem">The file system the level script and its includes are read from.</param>
    /// <param name="scriptPath">The path of the level script, relative to the game folder.</param>
    /// <param name="definitions">The sprite definitions.</param>
    /// <param name="ammoBatches">The rounds an ammunition pickup gives per weapon slot.</param>
    /// <param name="mapName">The name of the map as level scripts see it, in the form a script passes to <c>Load</c>, or null to use the script's path with a map extension.</param>
    /// <returns>The template.</returns>
    public static LevelTemplate FromModel(LevelModel model, IGameFileSystem fileSystem, string scriptPath, SpriteCatalog definitions, IReadOnlyList<int> ammoBatches, string? mapName = null) =>
        FromMapData(MapData.FromModel(model), fileSystem, scriptPath, definitions, ammoBatches) with { MapName = mapName ?? Path.ChangeExtension(scriptPath, MapExtension) };

    private static LevelTemplate FromMapData(MapData mapData, IGameFileSystem fileSystem, string scriptPath, SpriteCatalog definitions, IReadOnlyList<int> ammoBatches) => new()
    {
        MapData = mapData,
        Definitions = definitions,
        Movement = new MovementMap(mapData.Size),
        AmmoBatches = ammoBatches,
        FrameTimes = GameClock.DisplayFrameTimes,
        FileSystem = fileSystem,
        ScriptPath = scriptPath
    };
}
