namespace ASToolkit.Analysis.Survey;

/// <summary>
/// Represents the progression of a level through its phases.
/// </summary>
/// <param name="Phases">The phases in play order.</param>
/// <param name="Keys">The keys in landmark order.</param>
/// <param name="Locks">The places behind the gates.</param>
/// <param name="End">The place the level ends in.</param>
/// <param name="Flow">The places in the order the tour first enters them.</param>
/// <param name="Triggers">The trigger areas that spawn monsters.</param>
/// <param name="Seals">The ways back the triggers close.</param>
public sealed record LevelProgression(IReadOnlyList<LevelPhase> Phases, IReadOnlyList<PhaseKey> Keys, IReadOnlyList<int> Locks, int End, IReadOnlyList<int> Flow,
    IReadOnlyList<PhaseTrigger> Triggers, IReadOnlyList<LevelSeal> Seals)
{
    /// <summary>
    /// Returns the index of the phase that holds a place, or null when no phase holds it.
    /// </summary>
    /// <param name="place">The place number.</param>
    /// <returns>The index of the phase, or null if no phase holds the place.</returns>
    public int? GetPhase(int place)
    {
        foreach (var phase in Phases)
        {
            if (phase.Places.Contains(place))
            {
                return phase.Index;
            }
        }

        return null;
    }
}
