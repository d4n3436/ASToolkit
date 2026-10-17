using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Generation.Content;

/// <summary>
/// Represents a monster of a placed group, relative to the group's center.
/// </summary>
/// <param name="Id">The monster ID.</param>
/// <param name="Dx">The x offset.</param>
/// <param name="Dy">The y offset.</param>
/// <param name="Dir">The facing.</param>
public readonly record struct GroupMember(KnownObject Id, double Dx, double Dy, byte Dir);
