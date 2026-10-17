namespace ASToolkit.Core.Levels;

/// <summary>
/// Represents the straight run of wall edges around one edge, and whether its ends meet other walls.
/// </summary>
/// <param name="Before">The number of wall edges before the edge along the run.</param>
/// <param name="After">The number of wall edges after the edge along the run.</param>
/// <param name="StartsAtCorner">A value indicating whether another wall or door meets the start of the run.</param>
/// <param name="EndsAtCorner">A value indicating whether another wall or door meets the end of the run.</param>
public sealed record WallRun(int Before, int After, bool StartsAtCorner, bool EndsAtCorner)
{
    /// <summary>
    /// Finds the run of 'W' edges that an edge belongs to.
    /// </summary>
    public static WallRun Find(IReadOnlyDictionary<EdgeKey, char> edges, EdgeKey edge)
    {
        int before = 0;
        while (IsWall(Step(edge, -before - 1), edges))
        {
            before++;
        }

        int after = 0;
        while (IsWall(Step(edge, after + 1), edges))
        {
            after++;
        }

        var first = Step(edge, -before);
        var last = Step(edge, after);
        bool ends = edge.Axis == 'u' ? IsCorner(last.A, last.B + 1) : IsCorner(last.A + 1, last.B);
        
        return new WallRun(before, after, IsCorner(first.A, first.B), ends);

        bool IsCorner(int a, int b) => edge.Axis == 'u'
            ? edges.ContainsKey(new EdgeKey('v', a - 1, b)) || edges.ContainsKey(new EdgeKey('v', a, b))
            : edges.ContainsKey(new EdgeKey('u', a, b - 1)) || edges.ContainsKey(new EdgeKey('u', a, b));
    }

    /// <summary>
    /// Gets the number of wall pieces in the run.
    /// </summary>
    public int Length => Before + After + 1;

    /// <summary>
    /// Returns whether each end of the run meets a corner or lies at least the given number of edges away.
    /// </summary>
    public bool IsEnclosed(int openEndDistance) => (StartsAtCorner || Before >= openEndDistance) && (EndsAtCorner || After >= openEndDistance);
    
    private static bool IsWall(EdgeKey k, IReadOnlyDictionary<EdgeKey, char> edges) => edges.TryGetValue(k, out char kind) && kind == 'W';
    
    private static EdgeKey Step(EdgeKey k, int d) => k.Axis == 'u' ? new EdgeKey('u', k.A, k.B + d) : new EdgeKey('v', k.A + d, k.B);
}
