namespace ASToolkit.Core.Generation;

/// <summary>
/// Represents a generated level and where it goes in the random add-on.
/// </summary>
/// <param name="Number">The level's number in the add-on.</param>
/// <param name="Level">The generated level.</param>
/// <param name="TemplatePath">The original map the level is written over.</param>
public sealed record RandomAddOnLevel(int Number, GeneratedLevel Level, string TemplatePath);
