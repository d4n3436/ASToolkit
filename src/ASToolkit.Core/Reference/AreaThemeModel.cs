using ASToolkit.Core.Generation.Mission;

namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents the room sizes, halls, turns, row gaps, outline cuts, necks and connectors fitted to one floor theme of a style's originals.
/// </summary>
public sealed class AreaThemeModel
{
    private static readonly int[] SizeClassEdges = [5, 7, 12];

    /// <summary>
    /// Gets the room sizes by role, each a share of its level's room scale.
    /// </summary>
    public required IReadOnlyDictionary<MissionRole, IReadOnlyList<float>> RoleSizes { get; init; }

    /// <summary>
    /// Gets the room scales of the levels, each the median size in layout units of a level's rooms other than its halls, corridors and yards.
    /// </summary>
    public required IReadOnlyList<float> Scales { get; init; }

    /// <summary>
    /// Gets the map pixels of floor per layout unit.
    /// </summary>
    public required float PixelsPerUnit { get; init; }

    /// <summary>
    /// Gets the sizes of the halls on critical paths.
    /// </summary>
    public required IReadOnlyList<HallSize> Halls { get; init; }

    /// <summary>
    /// Gets the sizes of the courts that rings of rooms enclose.
    /// </summary>
    public required IReadOnlyList<CourtSize> Courts { get; init; }

    /// <summary>
    /// Gets the share of path rooms that are halls.
    /// </summary>
    public required float HallShare { get; init; }

    /// <summary>
    /// Gets the shares of straight steps, turns and returns along critical paths.
    /// </summary>
    public required TurnShares Turns { get; init; }

    /// <summary>
    /// Gets the share of steps along branch chains that turn.
    /// </summary>
    public required float BranchTurn { get; init; }

    /// <summary>
    /// Gets the gaps in layout units between neighbors along a hall side.
    /// </summary>
    public required IReadOnlyList<float> RowGaps { get; init; }

    /// <summary>
    /// Gets the cut outline share and class weights per short-side size class.
    /// </summary>
    public required IReadOnlyList<CutShare> CutShares { get; init; }

    /// <summary>
    /// Gets the percentage of the bounding box each cut outline class removes.
    /// </summary>
    public required IReadOnlyDictionary<OutlineClass, LogNormalFit> CutSizes { get; init; }

    /// <summary>
    /// Gets the neck fit, or null for a theme without necks.
    /// </summary>
    public required NeckFit? Neck { get; init; }

    /// <summary>
    /// Gets the connector fit, or null for a theme without connectors.
    /// </summary>
    public required ConnectorFit? Connector { get; init; }

    /// <summary>
    /// Returns the size class of a room's short side in layout units.
    /// </summary>
    public static int GetSizeClass(float shortSide) => SizeClassEdges.Count(edge => shortSide >= edge);

    /// <summary>
    /// Gets the number of short-side size classes.
    /// </summary>
    public static int SizeClassCount => SizeClassEdges.Length + 1;
}
