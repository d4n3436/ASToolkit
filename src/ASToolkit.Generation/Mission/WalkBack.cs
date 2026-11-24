namespace ASToolkit.Generation.Mission;

/// <summary>
/// Represents the walk from the last key of a level to the main gate into the next phase.
/// </summary>
/// <param name="Doors">The number of doors from the key to the gate's room.</param>
/// <param name="Rooms">The rooms the walk back passes, from the key on.</param>
/// <param name="IsRecrossing">A value indicating whether the walk back passes rooms of the way to the key.</param>
public sealed record WalkBack(int Doors, IReadOnlyList<int> Rooms, bool IsRecrossing);
