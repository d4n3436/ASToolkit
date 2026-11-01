namespace ASToolkit.Core.Survey;

/// <summary>
/// Represents something that happens to the player at one point of a level's tour.
/// </summary>
/// <param name="Kind">The kind of the beat.</param>
/// <param name="Section">The index of the section that holds the beat.</param>
/// <param name="Place">The place of the beat, or 0 when it has none.</param>
/// <param name="Route">The tour distance of the beat in pixels.</param>
/// <param name="Position">The share of its section's tour span before the beat, from 0 to 1.</param>
/// <param name="Intensity">The fight hit points per tour pixel around the beat.</param>
/// <param name="HitPoints">The monster hit points of a fight beat.</param>
/// <param name="Items">The pickups of a reward, stash or weapon beat.</param>
/// <param name="Depth">The doors between the beat's place and its section's body.</param>
public readonly record struct LevelBeat(BeatKind Kind, int Section, int Place, int Route, float Position, float Intensity, int HitPoints, int Items, int Depth);
