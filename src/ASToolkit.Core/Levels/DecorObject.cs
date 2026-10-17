using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Levels;

/// <summary>
/// Represents a dressing object of a level in lattice coordinates.
/// </summary>
/// <param name="Id">The object ID.</param>
/// <param name="U">The lattice u coordinate.</param>
/// <param name="V">The lattice v coordinate.</param>
/// <param name="Z">The height.</param>
/// <param name="Dir">The facing.</param>
/// <param name="Payload">The payload bytes.</param>
/// <param name="SupportIndex">The index of the object it stands on, -1 when it stands on nothing found, or null when it stands on the floor.</param>
public sealed record DecorObject(KnownObject Id, double U, double V, double Z, byte Dir, byte[] Payload, int? SupportIndex);
