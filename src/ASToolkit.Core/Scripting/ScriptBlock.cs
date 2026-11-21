using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Scripting;

/// <summary>
/// Represents an if block of a level script together with the conditions it waits for and the changes it makes.
/// </summary>
/// <param name="Conditions">The conditions of the block and the blocks around it, innermost last.</param>
/// <param name="Removals">The objects the block removes from the map.</param>
/// <param name="LosesLevel">A value indicating whether the block loses the level.</param>
/// <param name="WinsLevel">A value indicating whether the block wins the level.</param>
/// <param name="PlayerMove">The point the block moves the player to, or null.</param>
/// <param name="SetsDeathTimer">A value indicating whether the block sets a death timer on the player.</param>
/// <param name="ChangesLight">A value indicating whether the block changes the light.</param>
/// <param name="Assignments">The variables the block sets.</param>
public sealed record ScriptBlock(IReadOnlyList<ScriptCondition> Conditions, IReadOnlyList<KnownObject> Removals, bool LosesLevel, bool WinsLevel, Vector3? PlayerMove,
    bool SetsDeathTimer, bool ChangesLight, IReadOnlyList<ScriptAssignment> Assignments);
