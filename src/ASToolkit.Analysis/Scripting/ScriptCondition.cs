namespace ASToolkit.Analysis.Scripting;

/// <summary>
/// Represents what one condition of a level script's if block tests.
/// </summary>
/// <param name="Area">The area the player has to enter, or null.</param>
/// <param name="Quest">The line and step of the quest whose state the condition reads, or null.</param>
/// <param name="Variable">The variable the condition compares with a number, or null.</param>
/// <param name="Value">The number the variable is compared with.</param>
/// <param name="Flag">The variable the condition starts with as a flag, or null.</param>
/// <param name="WaitForSpace">A value indicating whether the condition waits for the space key.</param>
public readonly record struct ScriptCondition(TriggerArea? Area, (int Line, int Sub)? Quest, string? Variable, int Value, string? Flag, bool WaitForSpace);
