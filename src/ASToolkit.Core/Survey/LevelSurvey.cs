using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Survey;

/// <summary>
/// Represents the survey of a level from its map and route, with its places, connections and measures.
/// </summary>
public sealed class LevelSurvey
{
    private const int SearchCells = 10;

    private readonly Lazy<Dictionary<Cell, int>> _placeOf;

    /// <summary>
    /// Initializes a new instance of the <see cref="LevelSurvey"/> class.
    /// </summary>
    public LevelSurvey()
    {
        _placeOf = new Lazy<Dictionary<Cell, int>>(IndexPlaces);
    }

    /// <summary>
    /// Gets the map name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the theme of the level.
    /// </summary>
    public required LevelTheme Theme { get; init; }

    /// <summary>
    /// Gets the theme the level's floor shows, military for a level of lab walls on military floor.
    /// </summary>
    public required LevelTheme Setting { get; init; }

    /// <summary>
    /// Gets the gate style of the level.
    /// </summary>
    public required LevelStyle Style { get; init; }

    /// <summary>
    /// Gets the route grid that the place cells and connection edges lie on.
    /// </summary>
    public required Lattice Lattice { get; init; }

    /// <summary>
    /// Gets the places in route order.
    /// </summary>
    public required IReadOnlyList<LevelPlace> Places { get; init; }

    /// <summary>
    /// Gets the connections between the places.
    /// </summary>
    public required IReadOnlyList<PlaceConnection> Connections { get; init; }

    /// <summary>
    /// Gets the numbers that compare the level with others.
    /// </summary>
    public required LevelSignature Signature { get; init; }

    /// <summary>
    /// Gets the stays of the route in each place, in route order.
    /// </summary>
    public required IReadOnlyList<RouteVisit> Route { get; init; }

    /// <summary>
    /// Gets the gates.
    /// </summary>
    public required IReadOnlyList<LevelGate> Gates { get; init; }

    /// <summary>
    /// Gets the landmarks in route order.
    /// </summary>
    public required IReadOnlyList<LevelLandmark> Landmarks { get; init; }

    /// <summary>
    /// Gets the fights in route order.
    /// </summary>
    public required IReadOnlyList<LevelEncounter> Encounters { get; init; }

    /// <summary>
    /// Gets the items in route order.
    /// </summary>
    public required IReadOnlyList<LevelItem> Items { get; init; }

    /// <summary>
    /// Gets the groups of places off the route.
    /// </summary>
    public required IReadOnlyList<LevelBranch> Branches { get; init; }

    /// <summary>
    /// Returns the openings of the connections as segments on the map.
    /// </summary>
    public Doorway[] GetDoorways() => Connections
        .SelectMany(connection => connection.Edges.Select(run => new Doorway(run.Axis == 'v'
            ? new MapSegment(Lattice.ToWorld(run.From, run.Line), Lattice.ToWorld(run.To + 1, run.Line))
            : new MapSegment(Lattice.ToWorld(run.Line, run.From), Lattice.ToWorld(run.Line, run.To + 1)), connection.Kind)))
        .ToArray();

    /// <summary>
    /// Returns the place at a map point or the nearest one within 10 cells, or 0 when there is none.
    /// </summary>
    /// <param name="x">The map x.</param>
    /// <param name="y">The map y.</param>
    public int FindPlace(float x, float y)
    {
        var at = Lattice.GetCell(x, y);
        if (_placeOf.Value.TryGetValue(at, out int found))
        {
            return found;
        }

        for (int radius = 1; radius < SearchCells; radius++)
        {
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int du = -radius; du <= radius; du++)
            {
                for (int dv = -radius; dv <= radius; dv++)
                {
                    var cell = new Cell(at.U + du, at.V + dv);
                    if (!_placeOf.Value.TryGetValue(cell, out int place))
                    {
                        continue;
                    }

                    var center = Lattice.GetCenter(cell);
                    float distance = MathF.Sqrt((center.X - x) * (center.X - x) + (center.Y - y) * (center.Y - y));
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = place;
                    }
                }
            }

            if (best != 0)
            {
                return best;
            }
        }

        return 0;
    }

    private Dictionary<Cell, int> IndexPlaces() => Places.SelectMany(place => place.Cells.Select(cell => (Cell: cell, place.Id))).ToDictionary(entry => entry.Cell, entry => entry.Id);
}
