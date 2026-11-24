using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Analysis.Scripting;

/// <summary>
/// Represents an if block of a level script together with the conditions it waits for and the changes it makes.
/// </summary>
/// <param name="Conditions">The conditions of the block and the blocks around it, innermost last.</param>
/// <param name="Removals">The objects the block removes from the map.</param>
/// <param name="ShouldLoseLevel">A value indicating whether the block loses the level.</param>
/// <param name="ShouldWinLevel">A value indicating whether the block wins the level.</param>
/// <param name="PlayerMove">The point the block moves the player to, or null.</param>
/// <param name="ShouldSetDeathTimer">A value indicating whether the block sets a death timer on the player.</param>
/// <param name="ShouldChangeLight">A value indicating whether the block changes the light.</param>
/// <param name="Assignments">The variables the block sets.</param>
public sealed record ScriptBlock(IReadOnlyList<ScriptCondition> Conditions, IReadOnlyList<KnownObject> Removals, bool ShouldLoseLevel, bool ShouldWinLevel, Vector3? PlayerMove,
    bool ShouldSetDeathTimer, bool ShouldChangeLight, IReadOnlyList<ScriptAssignment> Assignments);
