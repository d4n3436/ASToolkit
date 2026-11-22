using System.Runtime.CompilerServices;

namespace ASToolkit.Analysis.Levels;

/// <summary>
/// Represents the four cells next to a cell, each with the edge crossed to reach it.
/// </summary>
[InlineArray(4)]
public struct Neighborhood
{
    private (Cell Cell, EdgeKey Edge) _first;

    /// <summary>
    /// Determines whether any of the four edges is in a set of edges and, if a kind is given, has that kind.
    /// </summary>
    /// <param name="edges">The set of edges with their kinds.</param>
    /// <param name="kind">The kind the edge must have, or null for any kind.</param>
    /// <returns><see langword="true"/> if one of the edges is in the set; otherwise, <see langword="false"/>.</returns>
    public readonly bool HasEdge(IReadOnlyDictionary<EdgeKey, EdgeKind> edges, EdgeKind? kind = null)
    {
        foreach (var (_, edge) in this)
        {
            if (edges.TryGetValue(edge, out var k) && (kind is null || k == kind))
            {
                return true;
            }
        }

        return false;
    }
}
