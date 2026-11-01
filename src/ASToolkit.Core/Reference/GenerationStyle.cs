namespace ASToolkit.Core.Reference;

/// <summary>
/// Specifies whether a generator builds a ten-level campaign like the game's own or a shorter and harder five-level one like its add-ons, and
/// which original levels it fits its model to.
/// </summary>
public enum GenerationStyle
{
    /// <summary>
    /// The campaign's ten levels.
    /// </summary>
    Campaign,

    /// <summary>
    /// The add-ons' five levels, fitted to every original.
    /// </summary>
    AddOn
}
