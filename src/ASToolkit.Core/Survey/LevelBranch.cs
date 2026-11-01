namespace ASToolkit.Core.Survey;

/// <summary>
/// Represents connected places that the route never enters.
/// </summary>
/// <param name="Places">The place numbers.</param>
/// <param name="Entrances">The places on the route the branch opens off.</param>
/// <param name="AreaShare">Their share of the floor.</param>
/// <param name="Detour">The longest detour in map pixels.</param>
public sealed record LevelBranch(IReadOnlyList<int> Places, IReadOnlyList<int> Entrances, float AreaShare, int Detour);
