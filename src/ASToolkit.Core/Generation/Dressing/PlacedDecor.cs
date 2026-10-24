using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Generation.Dressing;

/// <summary>
/// Represents a dressing object copied into a generated level, in lattice coordinates.
/// </summary>
/// <param name="Id">The object ID.</param>
/// <param name="U">The lattice u coordinate.</param>
/// <param name="V">The lattice v coordinate.</param>
/// <param name="Z">The height.</param>
/// <param name="Dir">The facing.</param>
/// <param name="Payload">The payload bytes.</param>
/// <param name="Source">The source level and object index it was copied from.</param>
/// <param name="Support">The source level and object index of the object it stands on, or null.</param>
public sealed record PlacedDecor(KnownObject Id, float U, float V, float Z, byte Dir, byte[] Payload, (int Level, int Index) Source, (int Level, int Index)? Support);
