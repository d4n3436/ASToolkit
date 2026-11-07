using System.Drawing;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Design;

/// <summary>
/// Provides how much of a level repeats a set of original levels, by groups of pieces and by door graphs.
/// </summary>
public sealed class NoveltyMeasurer
{
    private const float GroupReach = 70;
    private const float Tolerance = 2;
    private const int MinGroup = 3;

    private readonly string[] _names;
    private readonly Dictionary<(KnownObject First, KnownObject Second, int Dx, int Dy), List<(int Original, PointF Anchor)>> _pairs = [];
    private readonly Dictionary<(int Original, KnownObject Id, int X, int Y), List<PointF>> _grid = [];
    private readonly (ulong Hash, int[] Degrees)[] _graphs;

    /// <summary>
    /// Initializes a new instance of the <see cref="NoveltyMeasurer"/> class.
    /// </summary>
    public NoveltyMeasurer(IReadOnlyList<(string Name, LevelModel Model, LevelSurvey Survey)> originals)
    {
        _names = originals
            .Select(original => original.Name)
            .ToArray();

        _graphs = originals
            .Select(original => DoorGraph.Create(original.Survey))
            .Select(graph => (graph.ComputeShapeHash(), graph.GetDegrees()))
            .ToArray();

        for (int original = 0; original < originals.Count; original++)
        {
            var pieces = GetPieces(originals[original].Model);
            foreach (var piece in pieces)
            {
                var key = (original, piece.Id, (int)MathF.Round(piece.Point.X), (int)MathF.Round(piece.Point.Y));
                if (!_grid.TryGetValue(key, out var points))
                {
                    points = [];
                    _grid[key] = points;
                }

                points.Add(piece.Point);
            }

            foreach (var (first, second) in FindPairs(pieces).Where(pair => pair.First.Id != pair.Second.Id))
            {
                var key = (first.Id, second.Id, (int)MathF.Round(second.Point.X - first.Point.X), (int)MathF.Round(second.Point.Y - first.Point.Y));
                if (!_pairs.TryGetValue(key, out var anchors))
                {
                    anchors = [];
                    _pairs[key] = anchors;
                }

                anchors.Add((original, first.Point));
            }
        }
    }

    /// <summary>
    /// Returns the decor pieces other than blood and shadows, and the monsters, of a level that belong to a group of three or more, of two ids or more, matching an original other than the named one within 2 px.
    /// </summary>
    public MeasureShare CountCopiedPieces(string name, LevelModel model)
    {
        int self = Array.IndexOf(_names, name);
        var pieces = GetPieces(model);
        var neighbors = pieces.ToDictionary(piece => piece, _ => new List<(KnownObject Id, PointF Point)>());
        foreach (var (first, second) in FindPairs(pieces))
        {
            neighbors[first].Add(second);
        }

        var copied = new HashSet<(KnownObject Id, PointF Point)>();
        foreach (var first in pieces)
        {
            foreach (var second in neighbors[first].Where(second => second.Id != first.Id))
            {
                foreach ((int original, var anchor) in FindAnchors(first, second))
                {
                    if (original == self)
                    {
                        continue;
                    }

                    var group = neighbors[first]
                        .Where(other => HasPiece(original, other.Id, anchor.X + other.Point.X - first.Point.X, anchor.Y + other.Point.Y - first.Point.Y))
                        .ToArray();

                    if (group.Length + 1 >= MinGroup)
                    {
                        copied.Add(first);
                        copied.UnionWith(group);
                    }
                }
            }
        }

        return new MeasureShare(copied.Count, pieces.Length);
    }

    /// <summary>
    /// Returns the name of an original other than the named one whose door graph has the same shape as the level's, or null when none has.
    /// </summary>
    public string? FindEqualDoorGraph(string name, LevelSurvey survey)
    {
        var graph = DoorGraph.Create(survey);
        ulong hash = graph.ComputeShapeHash();
        int[] degrees = graph.GetDegrees();
        for (int original = 0; original < _graphs.Length; original++)
        {
            if (_names[original] != name && _graphs[original].Hash == hash && _graphs[original].Degrees.SequenceEqual(degrees))
            {
                return _names[original];
            }
        }

        return null;
    }

    private static (KnownObject Id, PointF Point)[] GetPieces(LevelModel model) => model.Sprites
        .Where(sprite => (sprite.Id is { IsDecor: true, IsBlood: false } && sprite.Id != KnownObject.EmbeddedShadow) || KnownObject.Monsters.Contains(sprite.Id) || KnownObject.Bosses.Contains(sprite.Id))
        .Select(sprite => (sprite.Id, new PointF(sprite.Position.X, sprite.Position.Y)))
        .Distinct()
        .ToArray();

    private static IEnumerable<((KnownObject Id, PointF Point) First, (KnownObject Id, PointF Point) Second)> FindPairs((KnownObject Id, PointF Point)[] pieces)
    {
        var buckets = pieces.ToLookup(piece => ((int)MathF.Floor(piece.Point.X / GroupReach), (int)MathF.Floor(piece.Point.Y / GroupReach)));
        foreach (var first in pieces)
        {
            int column = (int)MathF.Floor(first.Point.X / GroupReach);
            int row = (int)MathF.Floor(first.Point.Y / GroupReach);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    foreach (var second in buckets[(column + dx, row + dy)])
                    {
                        float x = second.Point.X - first.Point.X;
                        float y = second.Point.Y - first.Point.Y;
                        if (second != first && x * x + y * y <= GroupReach * GroupReach)
                        {
                            yield return (first, second);
                        }
                    }
                }
            }
        }
    }

    private IEnumerable<(int Original, PointF Anchor)> FindAnchors((KnownObject Id, PointF Point) first, (KnownObject Id, PointF Point) second)
    {
        int x = (int)MathF.Round(second.Point.X - first.Point.X);
        int y = (int)MathF.Round(second.Point.Y - first.Point.Y);
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (_pairs.TryGetValue((first.Id, second.Id, x + dx, y + dy), out var anchors))
                {
                    foreach (var anchor in anchors)
                    {
                        yield return anchor;
                    }
                }
            }
        }
    }

    private bool HasPiece(int original, KnownObject id, float x, float y)
    {
        for (int dx = -(int)Tolerance; dx <= Tolerance; dx++)
        {
            for (int dy = -(int)Tolerance; dy <= Tolerance; dy++)
            {
                if (_grid.TryGetValue((original, id, (int)MathF.Round(x) + dx, (int)MathF.Round(y) + dy), out var points)
                    && points.Any(point => MathF.Abs(point.X - x) <= Tolerance && MathF.Abs(point.Y - y) <= Tolerance))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
