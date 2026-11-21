namespace ASToolkit.Core.Levels;

/// <summary>
/// Provides the searches over a graph given as the neighbors of each node.
/// </summary>
public static class GraphExtensions
{
    extension<TNeighbors>(IReadOnlyDictionary<int, TNeighbors> neighbors) where TNeighbors : IEnumerable<int>
    {
        /// <summary>
        /// Returns the number of steps from the nearest source to every node reachable from the sources, through the nodes a filter keeps when
        /// one is given.
        /// </summary>
        /// <param name="sources">The nodes to start from.</param>
        /// <param name="within">The filter for the nodes to pass through, or null to pass through any node.</param>
        /// <returns>The number of steps to each reachable node.</returns>
        public Dictionary<int, int> MeasureHops(IEnumerable<int> sources, Func<int, bool>? within = null)
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
                if (!neighbors.TryGetValue(node, out var nexts))
                {
                    continue;
                }

                foreach (int next in nexts)
                {
                    if ((within is null || within(next)) && hops.TryAdd(next, hops[node] + 1))
                    {
                        queue.Enqueue(next);
                    }
                }
            }

            return hops;
        }

        /// <summary>
        /// Returns the connected parts of the graph restricted to the given nodes, in the order of their lowest nodes.
        /// </summary>
        /// <param name="nodes">The nodes to restrict the graph to.</param>
        /// <returns>The connected parts.</returns>
        public List<HashSet<int>> FindComponents(IReadOnlySet<int> nodes)
        {
            var seen = new HashSet<int>();
            var components = new List<HashSet<int>>();
            foreach (int node in nodes.Order())
            {
                if (seen.Contains(node))
                {
                    continue;
                }

                var component = neighbors.MeasureHops([node], nodes.Contains).Keys.ToHashSet();
                seen.UnionWith(component);
                components.Add(component);
            }

            return components;
        }

        /// <summary>
        /// Returns the nodes of a shortest path between two nodes, both included, through the nodes a filter keeps when one is given, or an empty
        /// list if there isn't a path between them.
        /// </summary>
        /// <param name="from">The node to start from.</param>
        /// <param name="to">The node to reach.</param>
        /// <param name="within">The filter for the nodes to pass through, or null to pass through any node.</param>
        /// <returns>The nodes of the path, or an empty list if there isn't a path.</returns>
        public List<int> FindPath(int from, int to, Func<int, bool>? within = null)
        {
            var previous = new Dictionary<int, int> { [from] = from };
            var queue = new Queue<int>([from]);
            while (queue.Count > 0 && !previous.ContainsKey(to))
            {
                int node = queue.Dequeue();
                foreach (int next in neighbors[node])
                {
                    if ((within is null || within(next)) && previous.TryAdd(next, node))
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
