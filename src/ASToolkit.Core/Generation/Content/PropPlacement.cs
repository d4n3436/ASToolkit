using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Generation.Content;

/// <summary>
/// Represents a prop of an original breaker room, relative to the breaker.
/// </summary>
/// <param name="Id">The object ID.</param>
/// <param name="Dx">The x offset.</param>
/// <param name="Dy">The y offset.</param>
/// <param name="Z">The height.</param>
/// <param name="Dir">The facing.</param>
public sealed record PropPlacement(KnownObject Id, double Dx, double Dy, double Z, byte Dir);
