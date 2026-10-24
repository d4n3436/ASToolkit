namespace ASToolkit.Core.Generation.Content;

/// <summary>
/// Represents an original dynamite site, the 179 marker with what its blast shuts down.
/// </summary>
/// <param name="Z">The marker height.</param>
/// <param name="Dir">The marker facing.</param>
/// <param name="Rank">The highest endless generator of the site, 800 being rank 0.</param>
/// <param name="Members">The pads and generators around the marker.</param>
public sealed record SiteTemplate(float Z, byte Dir, int Rank, IReadOnlyList<SiteMember> Members);
