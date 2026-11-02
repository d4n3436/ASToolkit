namespace ASToolkit.Core.Survey;

/// <summary>
/// Represents one state of a level between two state changes, with the critical path through it, its branches and its loops, or a return pass
/// back over earlier rooms to the objectives a key opened.
/// </summary>
/// <param name="Index">The index of the phase in play order.</param>
/// <param name="Places">The places of the phase.</param>
/// <param name="Entrance">The place the phase is entered by, or the key place of a return pass.</param>
/// <param name="Keys">The places of the keys the phase holds, in tour order.</param>
/// <param name="Path">The critical path from the entrance through the charges and objectives to the keys and the way on.</param>
/// <param name="Back">The part of the path after the last key, back to the lock it opens.</param>
/// <param name="Branches">The room trees off the critical path.</param>
/// <param name="Loops">The independent cycles among the phase's places.</param>
/// <param name="IsReturnPass">A value indicating whether the phase is a pass back over earlier rooms to the rooms its key opened.</param>
/// <param name="Revisits">The earlier places a return pass walks through.</param>
/// <param name="Sites">The objective places a return pass threads.</param>
public sealed record LevelPhase(int Index, IReadOnlyList<int> Places, int Entrance, IReadOnlyList<int> Keys, IReadOnlyList<int> Path, IReadOnlyList<int> Back,
    IReadOnlyList<PhaseBranch> Branches, int Loops, bool IsReturnPass, IReadOnlyList<int> Revisits, IReadOnlyList<int> Sites);
