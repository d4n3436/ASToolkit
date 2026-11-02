namespace ASToolkit.Core.Survey;

/// <summary>
/// Represents connected places of a phase off its critical path, with the path rooms they open from and the rewards and threat they hold.
/// </summary>
/// <param name="Places">The places of the branch.</param>
/// <param name="Anchors">The places the branch opens from, on the path when it has any there.</param>
/// <param name="Depths">The doors from the nearest anchor to each place of the branch.</param>
/// <param name="Rewards">The pickups of the branch.</param>
/// <param name="IsToured">A value indicating whether the tour enters the branch.</param>
/// <param name="ThreatHitPoints">The monster hit points of the branch.</param>
public sealed record PhaseBranch(IReadOnlyList<int> Places, IReadOnlyList<int> Anchors, IReadOnlyDictionary<int, int> Depths, int Rewards, bool IsToured, int ThreatHitPoints)
{
    /// <summary>
    /// Gets the doors from the anchors to the deepest place of the branch.
    /// </summary>
    public int Depth => Depths.Count == 0 ? 0 : Depths.Values.Max();
}
