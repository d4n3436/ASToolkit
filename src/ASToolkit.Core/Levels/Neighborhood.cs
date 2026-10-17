using System.Runtime.CompilerServices;

namespace ASToolkit.Core.Levels;

/// <summary>
/// Represents the four cells next to a cell, each with the edge crossed to reach it.
/// </summary>
[InlineArray(4)]
public struct Neighborhood
{
    private (Cell Cell, EdgeKey Edge) _first;

    /// <summary>
    /// Returns whether any of the four edges is in a set of edges, of a given kind when one is given.
    /// </summary>
    public readonly bool HasEdge(IReadOnlyDictionary<EdgeKey, char> edges, char? kind = null)
    {
        foreach (var (_, edge) in this)
        {
            if (edges.TryGetValue(edge, out char k) && (kind is null || k == kind))
            {
                return true;
            }
        }

        return false;
    }
}
