namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents the share of two-door rooms that are connectors and the connectors' sizes.
/// </summary>
/// <param name="Share">The share of two-door rooms that are connectors.</param>
/// <param name="Widths">The widths of connectors across their passage in layout units.</param>
/// <param name="Lengths">The lengths of connectors along their passage in layout units.</param>
public sealed record ConnectorFit(float Share, IReadOnlyList<float> Widths, IReadOnlyList<float> Lengths);
