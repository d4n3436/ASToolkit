namespace ASToolkit.Core.Levels;

/// <summary>
/// Provides the searches over a graph given as the neighbors of each node.
/// </summary>
public static class GraphExtensions
{
    extension<TNeighbors>(IReadOnlyDictionary<int, TNeighbors> neighbors) where TNeighbors : IEnumerable<int>
    {
        /// <summary>
        /// Returns the number of steps from the nearest source to every node reachable from the sources.
        /// </summary>
        public Dictionary<int, int> MeasureHops(IEnumerable<int> sources)
        {
            var hops = new Dictionary<int, int>();
            var queue = new Queue<int>();
            foreach (int source in sources)
            {
                if (hops.TryAdd(source, 0))
                {
                    queue.Enqueue(source);
                }
            }

            while (queue.Count > 0)
            {
                int node = queue.Dequeue();
                foreach (int next in neighbors[node])
                {
                    if (hops.TryAdd(next, hops[node] + 1))
                    {
                        queue.Enqueue(next);
                    }
                }
            }

            return hops;
        }

        /// <summary>
        /// Returns the nodes of a shortest path between two nodes, both included, or an empty list when no path joins them.
        /// </summary>
        public List<int> FindPath(int from, int to)
        {
            var previous = new Dictionary<int, int> { [from] = from };
            var queue = new Queue<int>([from]);
            while (queue.Count > 0 && !previous.ContainsKey(to))
            {
                int node = queue.Dequeue();
                foreach (int next in neighbors[node])
                {
                    if (previous.TryAdd(next, node))
                    {
                        queue.Enqueue(next);
                    }
                }
            }

            if (!previous.ContainsKey(to))
            {
                return [];
            }

            var path = new List<int> { to };
            while (path[^1] != from)
            {
                path.Add(previous[path[^1]]);
            }

            path.Reverse();
            return path;
        }
    }
}
