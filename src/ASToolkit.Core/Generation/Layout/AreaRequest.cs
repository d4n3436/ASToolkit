using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Layout;

/// <summary>
/// Represents the floor theme, the planned floor and the closets and pens an area build adds to a mission graph.
/// </summary>
/// <param name="Theme">The floor theme of the level.</param>
/// <param name="SecretClosets">The number of secret closets to add.</param>
/// <param name="SpawnClosets">The number of spawn closets to add.</param>
/// <param name="Pens">The number of gated leaf rooms to fence in as pens.</param>
/// <param name="FloorPixels">The planned floor of the level in map pixels.</param>
public readonly record struct AreaRequest(LevelTheme Theme, int SecretClosets, int SpawnClosets, int Pens, int FloorPixels);
