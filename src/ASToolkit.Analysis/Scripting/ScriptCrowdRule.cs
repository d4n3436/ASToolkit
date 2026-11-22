using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Analysis.Scripting;

/// <summary>
/// Represents a level script rule that keeps objects at fixed points while too many objects of one kind exist.
/// </summary>
/// <param name="Counted">The object whose count the rule watches.</param>
/// <param name="Max">The count above which the rule places its objects.</param>
/// <param name="Placed">The object the rule places.</param>
/// <param name="Points">The points the rule places its objects at.</param>
public sealed record ScriptCrowdRule(KnownObject Counted, int Max, KnownObject Placed, IReadOnlyList<Vector3> Points);
