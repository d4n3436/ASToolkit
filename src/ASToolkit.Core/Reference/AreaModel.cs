using System.Collections.Frozen;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents the architecture fitted to a style's originals for each indoor floor theme.
/// </summary>
public sealed class AreaModel
{
    private readonly FrozenDictionary<LevelTheme, AreaThemeModel> _themes;

    /// <summary>
    /// Initializes a new instance of the <see cref="AreaModel"/> class.
    /// </summary>
    /// <param name="themes">The architecture fitted to each indoor floor theme.</param>
    public AreaModel(IReadOnlyDictionary<LevelTheme, AreaThemeModel> themes) => _themes = themes.ToFrozenDictionary();

    /// <summary>
    /// Gets the architecture fitted to a floor theme.
    /// </summary>
    public AreaThemeModel GetTheme(LevelTheme theme) => _themes[theme];
}
