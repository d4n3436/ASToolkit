using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Output;

namespace ASToolkit.Core.Generation.Outdoor;

/// <summary>
/// Represents an outdoor level that the outdoor builder laid out.
/// </summary>
/// <param name="Map">The map of the outdoor level.</param>
/// <param name="Script">The script of the outdoor level.</param>
/// <param name="Provenance">The number of sprites of the outdoor level from each source.</param>
public sealed record OutdoorLevel(MapResourceBuilder Map, LevelScript Script, IReadOnlyDictionary<SpriteSource, int> Provenance);
