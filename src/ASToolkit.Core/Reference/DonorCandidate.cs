using ASToolkit.Core.Levels;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents an original level whose layout profile, fights, set pieces and decor a generated level borrows until models cover them, with the
/// theme, gate style and hit points it is chosen by.
/// </summary>
/// <param name="Name">The map name of the original.</param>
/// <param name="Theme">The theme of the original's walls.</param>
/// <param name="GateStyle">The gate style of the original.</param>
/// <param name="HitPoints">The hit points of the original's standing and spawned monsters.</param>
/// <param name="Money">The money of the original's cash pickups.</param>
public readonly record struct DonorCandidate(string Name, LevelTheme Theme, LevelStyle GateStyle, int HitPoints, int Money);
