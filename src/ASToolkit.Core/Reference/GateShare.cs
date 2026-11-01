using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents the share of a theme's original levels that one gate style closes, such as locked doors in half of the campaign's lab levels.
/// </summary>
/// <param name="GateStyle">The gate style.</param>
/// <param name="Share">The share of the theme's levels the gate style closes.</param>
public readonly record struct GateShare(LevelStyle GateStyle, float Share);
