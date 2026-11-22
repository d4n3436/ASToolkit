namespace ASToolkit.Analysis.Scripting;

/// <summary>
/// Represents a variable that an if block of a level script sets.
/// </summary>
/// <param name="Variable">The name of the variable.</param>
/// <param name="Value">The number the block stores, or 1 when it stores the result of an expression.</param>
public readonly record struct ScriptAssignment(string Variable, int Value);
