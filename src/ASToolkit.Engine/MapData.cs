using System.Drawing;
using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the contents of a map file loaded by the engine.
/// </summary>
public sealed class MapData
{
    /// <summary>
    /// Gets the sprites of the map, ordered by object ID.
    /// </summary>
    public required IReadOnlyList<MergedMapSprite> Sprites { get; init; }

    /// <summary>
    /// Gets the sprites of the map in the order the file stores them.
    /// </summary>
    public required IReadOnlyList<MergedMapSprite> StoredSprites { get; init; }

    /// <summary>
    /// Gets the size of the map in pixels.
    /// </summary>
    public SizeF Size { get; init; }

    /// <summary>
    /// Gets the camera position the map starts at.
    /// </summary>
    public Vector2 Camera { get; init; }

    /// <summary>
    /// Gets the weather and lighting effects the map starts with.
    /// </summary>
    public MapEnvironment Environment { get; init; }

    /// <summary>
    /// Gets the gamma the map starts with.
    /// </summary>
    public Gamma Ambient { get; init; }

    /// <summary>
    /// Gets the direction of the wind the map starts with.
    /// </summary>
    public Angle WindDirection { get; init; }

    /// <summary>
    /// Gets the speed of the wind the map starts with.
    /// </summary>
    public float WindSpeed { get; init; }

    /// <summary>
    /// Gets the sprite groups of the map, as stored sprite handles.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<int>> Groups { get; init; } = [];

    /// <summary>
    /// Gets the sprite handle of the player each player slot starts with, or 0 if the slot doesn't have a player.
    /// </summary>
    public IReadOnlyList<int> PlayerHandles { get; init; } = [];

    /// <summary>
    /// Gets the version of the map file.
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// Creates the contents of a parsed map file.
    /// </summary>
    /// <param name="map">The map file.</param>
    /// <returns>The contents.</returns>
    public static MapData FromMap(MapResourceBuilder map) => new()
    {
        Sprites = MergedMapSprite.OrderSprites(map.Sprites).ToArray(),
        StoredSprites = map.Sprites,
        Size = map.Size,
        Camera = map.Scroll,
        Environment = map.Graphics.Environment,
        Ambient = map.Graphics.Ambient,
        WindDirection = map.Graphics.Direction,
        WindSpeed = map.Graphics.DirectionLength,
        Groups = map.Groups.ToArray(),
        PlayerHandles = map.PlayerHandles,
        Version = map.Version
    };
}
