namespace ASToolkit.Core.Levels;

/// <summary>
/// Provides the searches over a set of cells.
/// </summary>
public static class CellSetExtensions
{
    extension(IReadOnlySet<Cell> cells)
    {
        /// <summary>
        /// Returns the cells reachable from sources without crossing a closed edge.
        /// </summary>
        /// <param name="sources">The cells to start from; those outside the set are skipped.</param>
        /// <param name="isClosed">Whether an edge stops the search.</param>
        public HashSet<Cell> Flood(IEnumerable<Cell> sources, Func<EdgeKey, bool> isClosed)
        {
            var seen = new HashSet<Cell>();
            var queue = new Queue<Cell>();
            foreach (var source in sources)
            {
                if (cells.Contains(source) && seen.Add(source))
                {
                    queue.Enqueue(source);
                }
            }

            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                foreach (var (neighbor, edge) in EdgeKey.GetNeighbors(cell))
                {
                    if (cells.Contains(neighbor) && !isClosed(edge) && seen.Add(neighbor))
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }

            return seen;
        }
    }
}
