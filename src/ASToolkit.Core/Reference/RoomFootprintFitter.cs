using ASToolkit.Core.Levels;
using ASToolkit.Core.Rooms;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Reference;

/// <summary>
/// Provides the outlines of the originals' smaller office, lab, storage, computer and passage rooms on the building grid, the vocabulary the building
/// grammars transform their rooms from.
/// </summary>
public sealed class RoomFootprintFitter
{
    // A grid unit is two basement cells; a grid cell belongs to the room when three of its four cells do
    private const int CellsPerUnit = 2;
    private const int CoveredCells = 3;

    private const int MinArea = 9;
    private const int MaxArea = 100;

    private static readonly RoomType[] Types = [RoomType.Office, RoomType.Lab, RoomType.Storage, RoomType.ComputerRoom, RoomType.Passage];

    /// <summary>
    /// Reads the footprints of the basement originals among the given levels.
    /// </summary>
    public static IReadOnlyList<RoomFootprint> Fit(IReadOnlyList<ReferenceLevel> levels) => levels
        .Where(level => level.Survey.FloorSet == WallSet.Basement)
        .SelectMany(level => level.Atlas.Rooms)
        .Where(room => room.Place.Kind == PlaceKind.Room && Types.Contains(room.Type))
        .Select(room => Read(room.Place.Cells))
        .Where(footprint => footprint is not null)
        .Select(footprint => footprint!.Value)
        .ToArray();

    private static RoomFootprint? Read(IReadOnlyList<Cell> cells)
    {
        var units = cells
            .GroupBy(cell => new Cell((int)MathF.Floor(cell.U / (float)CellsPerUnit), (int)MathF.Floor(cell.V / (float)CellsPerUnit)))
            .Where(group => group.Count() >= CoveredCells)
            .Select(group => group.Key)
            .ToArray();

        if (units.Length is < MinArea or > MaxArea)
        {
            return null;
        }

        int u0 = units.Min(cell => cell.U);
        int v0 = units.Min(cell => cell.V);
        var shifted = units.Select(cell => new Cell(cell.U - u0, cell.V - v0)).ToHashSet();
        int width = shifted.Max(cell => cell.U) + 1;
        int height = shifted.Max(cell => cell.V) + 1;
        return shifted.Flood([shifted.First()], _ => false).Count == shifted.Count && width >= 3 && height >= 3 ? new RoomFootprint(width, height, shifted.OrderBy(cell => cell.V).ThenBy(cell => cell.U).ToArray()) : null;
    }
}
