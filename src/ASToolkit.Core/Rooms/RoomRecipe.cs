using System.Numerics;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Rooms;

/// <summary>
/// Represents how the original rooms of a type are furnished, as the parts they hold and the pieces scattered over them.
/// </summary>
public sealed class RoomRecipe
{
    /// <summary>
    /// The share of a type's rooms that must hold a kind of part for every room of the type to need one.
    /// </summary>
    public const float RequiredShare = 0.75f;

    private const float AreaUnit = 100_000;
    private const float RowTolerance = 12;

    private RoomRecipe()
    {
    }

    /// <summary>
    /// Gets the type of the rooms.
    /// </summary>
    public required RoomType Type { get; init; }

    /// <summary>
    /// Gets the number of original rooms measured.
    /// </summary>
    public required int Rooms { get; init; }

    /// <summary>
    /// Gets the smallest, median and largest floor area of the rooms in square map pixels.
    /// </summary>
    public required (int Min, int Median, int Max) Areas { get; init; }

    /// <summary>
    /// Gets the kinds of parts, the most frequent first.
    /// </summary>
    public required IReadOnlyList<RecipePart> Parts { get; init; }

    /// <summary>
    /// Gets the number of each scattered piece per 100,000 square map pixels of room floor.
    /// </summary>
    public required IReadOnlyDictionary<KnownObject, float> ScatterRates { get; init; }

    /// <summary>
    /// Measures a recipe for each type from the rooms of the originals, leaving out rooms furnished two ways.
    /// </summary>
    /// <param name="rooms">The rooms of the original levels.</param>
    /// <param name="lattice">A lattice with the axes of the rooms' walls.</param>
    public static IReadOnlyList<RoomRecipe> Measure(IEnumerable<AtlasRoom> rooms, Lattice lattice) => rooms
        .Where(room => room is { IsRoom: true, Side: null, Type: not (RoomType.Passage or RoomType.Bare or RoomType.Arena or RoomType.Closet) })
        .GroupBy(room => room.Type)
        .OrderBy(group => group.Key)
        .Select(group => Measure(group.Key, group.ToArray(), lattice)).ToArray();

    private static (float? Spacing, float RowShare) MeasureRows(IEnumerable<RoomPart[]> rooms, Lattice lattice)
    {
        var zero = lattice.ToWorld(0, 0);
        var axes = new[] { lattice.ToWorld(1, 0), lattice.ToWorld(0, 1) }
            .Select(end => Vector2.Normalize(new Vector2(end.X - zero.X, end.Y - zero.Y)))
            .ToArray();
        
        var nearest = rooms
            .Where(parts => parts.Length > 1)
            .SelectMany(parts => parts.Select(part => parts.Where(other => other != part)
                .Select(other => new Vector2(other.Origin.X - part.Origin.X, other.Origin.Y - part.Origin.Y))
                .MinBy(offset => offset.Length())))
            .ToArray();
        
        if (nearest.Length == 0)
        {
            return (null, 0);
        }

        float[] gaps = nearest
            .Select(offset => offset.Length())
            .Order()
            .ToArray();
        
        int rows = nearest.Count(offset => axes.Any(axis => MathF.Abs(offset.X * axis.Y - offset.Y * axis.X) <= RowTolerance));
        return (gaps[gaps.Length / 2], rows / (float)nearest.Length);
    }

    private static RoomRecipe Measure(RoomType type, AtlasRoom[] rooms, Lattice lattice)
    {
        float floor = rooms.Sum(room => room.Place.AreaPixels) / AreaUnit;
        
        int[] areas = rooms
            .Select(room => room.Place.AreaPixels)
            .Order()
            .ToArray();
        
        var parts = rooms.SelectMany(room => room.Parts.Select(part => (Room: room, Part: part)))
            .GroupBy(entry => entry.Part.Kind)
            .Select(group =>
            {
                (float? spacing, float rowShare) = MeasureRows(group.GroupBy(entry => entry.Room).Select(inRoom => inRoom.Select(entry => entry.Part).ToArray()), lattice);
                return new RecipePart
                {
                    SpotShares = group.GroupBy(entry => entry.Part.Spot).OrderBy(spots => spots.Key).ToDictionary(spots => spots.Key, spots => spots.Count() / (float)group.Count()),
                    Families = group.First().Part.Families,
                    Rate = group.Count() / floor,
                    RoomShare = group.Select(entry => entry.Room).Distinct().Count() / (float)rooms.Length,
                    Spacing = spacing,
                    RowShare = rowShare,
                    Examples = group.Select(entry => entry.Part).ToArray()
                };
            })
            .OrderByDescending(part => part.Rate)
            .ToArray();
        return new RoomRecipe
        {
            Type = type,
            Rooms = rooms.Length,
            Areas = (areas[0], areas[areas.Length / 2], areas[^1]),
            Parts = parts,
            ScatterRates = rooms.SelectMany(room => room.Scatter).GroupBy(pair => pair.Key).OrderBy(group => group.Key)
                .ToDictionary(group => group.Key, group => group.Sum(pair => pair.Value) / floor)
        };
    }
}
