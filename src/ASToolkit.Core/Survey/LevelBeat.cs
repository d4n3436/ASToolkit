namespace ASToolkit.Core.Survey;

/// <summary>
/// Represents a fight, key, lock, reward, set piece or goal at one point of a level's tour, with the phase that holds it, its place along that
/// phase and the fight intensity around it.
/// </summary>
/// <param name="Kind">The kind of the beat.</param>
/// <param name="Phase">The index of the phase that holds the beat.</param>
/// <param name="Place">The place of the beat, or 0 when it has none.</param>
/// <param name="Route">The tour distance of the beat in pixels.</param>
/// <param name="Position">The share of its phase's tour span before the beat, from 0 to 1.</param>
/// <param name="Intensity">The fight hit points per tour pixel around the beat.</param>
/// <param name="HitPoints">The monster hit points of a fight beat.</param>
/// <param name="Items">The pickups of a reward, stash or weapon beat.</param>
/// <param name="Depth">The doors between the beat's place and its phase's critical path.</param>
public readonly record struct LevelBeat(BeatKind Kind, int Phase, int Place, int Route, float Position, float Intensity, int HitPoints, int Items, int Depth);
