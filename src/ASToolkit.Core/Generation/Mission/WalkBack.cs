namespace ASToolkit.Core.Generation.Mission;

/// <summary>
/// Represents the walk from a level's last key to the main gate into the next phase, with its length, the rooms it passes and whether it crosses
/// the way to the key again.
/// </summary>
/// <param name="Doors">The number of doors from the key to the gate's room.</param>
/// <param name="Rooms">The rooms the walk back passes, from the key on.</param>
/// <param name="Recrosses">A value indicating whether the walk back passes rooms of the way to the key.</param>
public sealed record WalkBack(int Doors, IReadOnlyList<int> Rooms, bool Recrosses);
