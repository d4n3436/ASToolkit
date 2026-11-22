namespace ASToolkit.Analysis.Reference;

/// <summary>
/// Provides the values that depend on a <see cref="GenerationStyle"/>, such as the number of levels of its campaign.
/// </summary>
public static class GenerationStyleExtensions
{
    extension(GenerationStyle style)
    {
        /// <summary>Gets the number of levels of a campaign of the style, which is 10 for the campaign and 5 for an add-on.</summary>
        public int LevelCount => style == GenerationStyle.AddOn ? 5 : 10;
    }
}
