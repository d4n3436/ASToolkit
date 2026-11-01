namespace ASToolkit.Core.Reference;

/// <summary>
/// Provides the game rules that depend on a <see cref="GenerationStyle"/>, such as the number of levels of its campaign.
/// </summary>
public static class GenerationStyleExtensions
{
    extension(GenerationStyle style)
    {
        /// <summary>Gets the number of levels a campaign of the style has, the campaign's 10 or an add-on's 5.</summary>
        public int LevelCount => style == GenerationStyle.AddOn ? 5 : 10;
    }
}
