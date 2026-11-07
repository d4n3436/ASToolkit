using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Survey;

/// <summary>
/// Represents consecutive grid edges on one line.
/// </summary>
/// <param name="Axis">The edge axis, 'u' or 'v'.</param>
/// <param name="Line">The fixed coordinate.</param>
/// <param name="From">The first varying coordinate.</param>
/// <param name="To">The last varying coordinate.</param>
public readonly record struct EdgeRun(char Axis, int Line, int From, int To)
{
    /// <summary>
    /// Enumerates over the edges.
    /// </summary>
    public IEnumerable<EdgeKey> GetEdges()
    {
        for (int position = From; position <= To; position++)
        {
            yield return Axis == 'v' ? new EdgeKey('v', position, Line) : new EdgeKey('u', Line, position);
        }
    }

    /// <summary>
    /// Returns the runs that cover a set of edges.
    /// </summary>
    public static EdgeRun[] Create(IEnumerable<EdgeKey> edges)
    {
        var runs = new List<EdgeRun>();
        var sorted = edges
            .Select(edge => (edge.Axis, Line: edge.Axis == 'v' ? edge.B : edge.A, Position: edge.Axis == 'v' ? edge.A : edge.B))
            .Distinct()
            .OrderBy(edge => edge.Axis)
            .ThenBy(edge => edge.Line)
            .ThenBy(edge => edge.Position);

        foreach (var edge in sorted)
        {
            if (runs.Count > 0 && runs[^1].Axis == edge.Axis && runs[^1].Line == edge.Line && runs[^1].To == edge.Position - 1)
            {
                runs[^1] = runs[^1] with { To = edge.Position };
            }
            else
            {
                runs.Add(new EdgeRun(edge.Axis, edge.Line, edge.Position, edge.Position));
            }
        }

        return runs.ToArray();
    }
}
