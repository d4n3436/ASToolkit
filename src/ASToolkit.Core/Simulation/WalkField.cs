using System.Numerics;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Simulation;

internal sealed class WalkField
{
    private const int Margin = 2;
    private const int Sides = 4;
    private const float MinDelta = 1e-6f;

    private readonly RouteGrid _grid;
    private readonly Lattice _lattice;
    private readonly int _minU;
    private readonly int _minV;
    private readonly int _width;
    private readonly int _height;
    private readonly int[] _cells;
    private readonly int[] _places;
    private readonly byte[] _sightWalls;
    private readonly int[] _neighbors;
    private readonly int[] _distance;
    private readonly int[] _queue;
    private readonly int[] _fieldQueue;
    private int _reached;

    public WalkField(RouteGrid grid, bool doorsLocked, bool fencesUp)
    {
        _grid = grid;
        _lattice = grid.Lattice;
        _minU = grid.Cells.Min(cell => cell.U) - Margin;
        _minV = grid.Cells.Min(cell => cell.V) - Margin;
        _width = grid.Cells.Max(cell => cell.U) + Margin - _minU + 1;
        _height = grid.Cells.Max(cell => cell.V) + Margin - _minV + 1;
        _cells = new int[_width * _height];
        Array.Fill(_cells, -1);
        _places = new int[grid.Cells.Count];
        int index = 0;
        foreach (var cell in grid.Cells)
        {
            int place = (cell.V - _minV) * _width + (cell.U - _minU);
            _cells[place] = index;
            _places[index++] = place;
        }

        _sightWalls = new byte[_width * _height];
        _neighbors = new int[_places.Length * Sides];
        _distance = new int[_places.Length];
        _queue = new int[_places.Length];
        _fieldQueue = new int[_places.Length];
        SetGates(doorsLocked, fencesUp);
    }

    public float CellStep => _lattice.CellStep;

    public void SetGates(bool doorsLocked, bool fencesUp)
    {
        for (int place = 0; place < _sightWalls.Length; place++)
        {
            var neighbors = EdgeKey.GetNeighbors(new Cell(place % _width + _minU, place / _width + _minV));
            int index = _cells[place];
            byte walls = 0;
            for (int side = 0; side < Sides; side++)
            {
                var (next, edge) = neighbors[side];
                bool hasKind = _grid.Edges.TryGetValue(edge, out var kind);
                if (hasKind && (kind.ShowsWall || kind == EdgeKind.ShutDoor || (doorsLocked && kind == EdgeKind.Door)))
                {
                    walls |= (byte)(1 << side);
                }

                if (index < 0)
                    continue;
                
                bool blocks = (fencesUp && _grid.FenceEdges.Contains(edge)) ||
                              (hasKind && (kind.BlocksWalking || kind == EdgeKind.SecretWall || (doorsLocked && kind == EdgeKind.Door)));
                _neighbors[index * Sides + side] = blocks ? -1 : Find(next);
            }

            _sightWalls[place] = walls;
        }

        Array.Fill(_distance, -1);
    }

    public int Find(Vector2 point) => Find(_lattice.GetCell(point.X, point.Y));

    public int Find(Cell cell)
    {
        int u = cell.U - _minU;
        int v = cell.V - _minV;
        return u >= 0 && v >= 0 && u < _width && v < _height ? _cells[v * _width + u] : -1;
    }

    public Vector2 GetCenter(int cell)
    {
        int place = _places[cell];
        var center = _lattice.GetCenter(new Cell(place % _width + _minU, place / _width + _minV));
        return new Vector2(center.X, center.Y);
    }

    public int GetDistance(int cell) => _distance[cell];

    // Fill walking steps from the source out to a step limit and leave farther cells unreached
    public void Flood(int source, int maxSteps) => _reached = Flood(source, maxSteps, _distance, _queue);

    public int ReachedCount => _reached;

    // Return the cells of the last flood from the player in the order it reached them
    public int GetReached(int order) => _queue[order];

    public int[] CreateField(int target)
    {
        int[] distance = new int[_places.Length];
        Flood(target, int.MaxValue, distance, _fieldQueue);
        return distance;
    }

    public int GetDownhill(int cell) => GetDownhill(cell, _distance);

    public int GetDownhill(int cell, int[] distance)
    {
        int best = -1;
        int bestDistance = distance[cell];
        for (int side = 0; side < Sides; side++)
        {
            int next = _neighbors[cell * Sides + side];
            if (next < 0 || distance[next] < 0 || distance[next] >= bestDistance)
                continue;
            
            best = next;
            bestDistance = distance[next];
        }

        return best;
    }

    public bool CanSee(Vector2 from, Vector2 to) => TraceSight(from, to) >= 1;

    private int Flood(int source, int maxSteps, int[] distance, int[] queue)
    {
        Array.Fill(distance, -1);
        if (source < 0)
        {
            return 0;
        }

        int head = 0;
        int tail = 0;
        distance[source] = 0;
        queue[tail++] = source;
        while (head < tail)
        {
            int cell = queue[head++];
            int steps = distance[cell] + 1;
            if (steps > maxSteps)
                continue;

            for (int side = 0; side < Sides; side++)
            {
                int next = _neighbors[cell * Sides + side];
                if (next < 0 || distance[next] >= 0)
                    continue;
                
                distance[next] = steps;
                queue[tail++] = next;
            }
        }

        return tail;
    }

    // Walks the cells the segment crosses and return the share of it before the first wall that blocks the sight
    public float TraceSight(Vector2 from, Vector2 to)
    {
        var start = _lattice.ToLattice(from.X, from.Y);
        var end = _lattice.ToLattice(to.X, to.Y);
        float du = end.U - start.U;
        float dv = end.V - start.V;
        int u = (int)MathF.Floor(start.U);
        int v = (int)MathF.Floor(start.V);
        int stepU = du > 0 ? 1 : -1;
        int stepV = dv > 0 ? 1 : -1;
        bool movesU = MathF.Abs(du) > MinDelta;
        bool movesV = MathF.Abs(dv) > MinDelta;
        float deltaU = movesU ? MathF.Abs(1 / du) : float.MaxValue;
        float deltaV = movesV ? MathF.Abs(1 / dv) : float.MaxValue;
        float nextU = movesU ? (du > 0 ? u + 1 - start.U : start.U - u) * deltaU : float.MaxValue;
        float nextV = movesV ? (dv > 0 ? v + 1 - start.V : start.V - v) * deltaV : float.MaxValue;
        while (true)
        {
            bool alongU = nextU < nextV;
            float share = alongU ? nextU : nextV;
            if (share > 1)
            {
                return 1;
            }

            if (IsWall(u, v, alongU ? (stepU > 0 ? 0 : 1) : (stepV > 0 ? 2 : 3)))
            {
                return share;
            }

            if (alongU)
            {
                u += stepU;
                nextU += deltaU;
            }
            else
            {
                v += stepV;
                nextV += deltaV;
            }
        }
    }

    private bool IsWall(int u, int v, int side)
    {
        u -= _minU;
        v -= _minV;
        return u >= 0 && v >= 0 && u < _width && v < _height && (_sightWalls[v * _width + u] & (1 << side)) != 0;
    }
}
