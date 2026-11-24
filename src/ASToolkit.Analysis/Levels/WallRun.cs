namespace ASToolkit.Analysis.Levels;

/// <summary>
/// Represents the straight run of wall edges around one edge, and whether its ends meet other walls.
/// </summary>
/// <param name="Before">The number of wall edges before the edge along the run.</param>
/// <param name="After">The number of wall edges after the edge along the run.</param>
/// <param name="HasCornerAtStart">A value indicating whether another wall or door meets the start of the run.</param>
/// <param name="HasCornerAtEnd">A value indicating whether another wall or door meets the end of the run.</param>
public sealed record WallRun(int Before, int After, bool HasCornerAtStart, bool HasCornerAtEnd)
{
    /// <summary>
    /// Finds the run of wall edges that an edge belongs to.
    /// </summary>
    /// <param name="edges">The edges with their kinds.</param>
    /// <param name="edge">The edge.</param>
    /// <returns>The run the edge belongs to.</returns>
    public static WallRun Find(IReadOnlyDictionary<EdgeKey, EdgeKind> edges, EdgeKey edge)
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
        bool hasCornerAtEnd = edge.Axis == 'u' ? IsCorner(last.A, last.B + 1) : IsCorner(last.A + 1, last.B);
        
        return new WallRun(before, after, IsCorner(first.A, first.B), hasCornerAtEnd);

        bool IsCorner(int a, int b) => edge.Axis == 'u'
            ? edges.ContainsKey(new EdgeKey('v', a - 1, b)) || edges.ContainsKey(new EdgeKey('v', a, b))
            : edges.ContainsKey(new EdgeKey('u', a, b - 1)) || edges.ContainsKey(new EdgeKey('u', a, b));
    }

    /// <summary>
    /// Gets the number of wall pieces in the run.
    /// </summary>
    public int Length => Before + After + 1;

    /// <summary>
    /// Determines whether each end of the run meets a corner or lies at least the given number of edges away.
    /// </summary>
    /// <param name="openEndDistance">The number of edges an end must lie away if it doesn't meet a corner.</param>
    /// <returns><see langword="true"/> if both ends of the run meet a corner or lie far enough away; otherwise, <see langword="false"/>.</returns>
    public bool IsEnclosed(int openEndDistance) => (HasCornerAtStart || Before >= openEndDistance) && (HasCornerAtEnd || After >= openEndDistance);
    
    private static bool IsWall(EdgeKey k, IReadOnlyDictionary<EdgeKey, EdgeKind> edges) => edges.TryGetValue(k, out var kind) && kind == EdgeKind.Wall;
    
    private static EdgeKey Step(EdgeKey k, int d) => k.Axis == 'u' ? new EdgeKey('u', k.A, k.B + d) : new EdgeKey('v', k.A + d, k.B);
}
