using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Generation.Content;

/// <summary>
/// Represents an object of an original dynamite site, relative to its 179 marker.
/// </summary>
/// <param name="Id">The object ID.</param>
/// <param name="Dx">The x offset.</param>
/// <param name="Dy">The y offset.</param>
/// <param name="Z">The height.</param>
/// <param name="Dir">The facing.</param>
/// <param name="Side">The side.</param>
/// <param name="Payload">The payload bytes.</param>
public sealed record SiteMember(KnownObject Id, double Dx, double Dy, double Z, byte Dir, uint Side, byte[] Payload);
