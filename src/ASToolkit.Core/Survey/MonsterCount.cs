using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Survey;

/// <summary>
/// Represents the number of monsters of one ID.
/// </summary>
/// <param name="Id">The object ID.</param>
/// <param name="Count">The number of monsters.</param>
public readonly record struct MonsterCount(KnownObject Id, int Count);
