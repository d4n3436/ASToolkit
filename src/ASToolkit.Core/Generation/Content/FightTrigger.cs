namespace ASToolkit.Core.Generation.Content;

/// <summary>
/// Represents the trigger of an ambush on a doorway every walk to the end crosses.
/// </summary>
/// <param name="Region">The plan region the ambush belongs to.</param>
/// <param name="Link">The plan link whose doorway holds the trigger.</param>
public readonly record struct FightTrigger(int Region, int Link);
