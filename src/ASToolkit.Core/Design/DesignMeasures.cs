using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Design;

/// <summary>
/// Represents the design measures of a level.
/// </summary>
public sealed class DesignMeasures
{
    /// <summary>
    /// Gets the signature of the level's survey.
    /// </summary>
    public required LevelSignature Signature { get; init; }

    /// <summary>
    /// Gets chairs within 60 px of a table.
    /// </summary>
    public required MeasureShare ChairsAtTables { get; init; }

    /// <summary>
    /// Gets tables with a chair within 60 px.
    /// </summary>
    public required MeasureShare TablesWithChairs { get; init; }

    /// <summary>
    /// Gets test tubes within 60 px of a table.
    /// </summary>
    public required MeasureShare TubesOnTables { get; init; }

    /// <summary>
    /// Gets lab equipment within 60 px of a table.
    /// </summary>
    public required MeasureShare LabEquipmentOnTables { get; init; }

    /// <summary>
    /// Gets scattered paper within 60 px of a table.
    /// </summary>
    public required MeasureShare PaperOnTables { get; init; }

    /// <summary>
    /// Gets the computers, at any height, on floor cells with no wall.
    /// </summary>
    public required MeasureShare ComputersAwayFromWalls { get; init; }

    /// <summary>
    /// Gets big computers with a chair within 200 px.
    /// </summary>
    public required MeasureShare BigComputersWithChairs { get; init; }

    /// <summary>
    /// Gets corpses with a blood pool within 40 px.
    /// </summary>
    public required MeasureShare CorpsesOnBlood { get; init; }

    /// <summary>
    /// Gets computers, test tubes, lab equipment and scattered paper in halls, corridors, yards and nooks.
    /// </summary>
    public required MeasureShare OfficeDecorInPassages { get; init; }

    /// <summary>
    /// Gets computers, test tubes, lab equipment and scattered paper in places filled mostly with storage.
    /// </summary>
    public required MeasureShare OfficeDecorInStorage { get; init; }

    /// <summary>
    /// Gets the floor objects below height 5 with the walls of their cells.
    /// </summary>
    public required IReadOnlyList<WallSample> Walls { get; init; }

    /// <summary>
    /// Gets loose money in cells with a wall.
    /// </summary>
    public required MeasureShare MoneyAgainstWalls { get; init; }

    /// <summary>
    /// Gets loose money in corner cells.
    /// </summary>
    public required MeasureShare MoneyInCorners { get; init; }

    /// <summary>
    /// Gets loose health in cells with a wall.
    /// </summary>
    public required MeasureShare HealthAgainstWalls { get; init; }

    /// <summary>
    /// Gets loose ammo in cells with a wall.
    /// </summary>
    public required MeasureShare AmmoAgainstWalls { get; init; }

    /// <summary>
    /// Gets loose armor in cells with a wall.
    /// </summary>
    public required MeasureShare ArmorAgainstWalls { get; init; }

    /// <summary>
    /// Gets piles of loose pickups, 45 px apart, that hold two or more.
    /// </summary>
    public required MeasureShare PickupPiles { get; init; }

    /// <summary>
    /// Gets health pickups that lie after a fight on the route.
    /// </summary>
    public required MeasureShare HealthAfterFights { get; init; }

    /// <summary>
    /// Gets shelves with another shelf within 70 px.
    /// </summary>
    public required MeasureShare ShelvesGrouped { get; init; }

    /// <summary>
    /// Gets the most cabinets in one group, 70 px apart.
    /// </summary>
    public required int LongestCabinetRun { get; init; }

    /// <summary>
    /// Gets missiles in groups of exactly three, 120 px apart.
    /// </summary>
    public required MeasureShare MissilesInTriplets { get; init; }

    /// <summary>
    /// Gets groups of two or more teleport pads, 400 px apart, that mirror about their middle.
    /// </summary>
    public required MeasureShare MirroredPads { get; init; }

    /// <summary>
    /// Gets the wall inscriptions within 230 px of the mounted cannon, or null without a cannon.
    /// </summary>
    public required int? CannonStripes { get; init; }

    /// <summary>
    /// Gets the distance from the mounted cannon to the nearest solid object, or null without a cannon.
    /// </summary>
    public required int? CannonOpenPixels { get; init; }

    /// <summary>
    /// Gets the hit points of ambushes and placed groups within 1,200 px of the mounted cannon.
    /// </summary>
    public required MeasureShare CannonHitPoints { get; init; }

    /// <summary>
    /// Gets the walk from the start to the mounted cannon, or null without a cannon.
    /// </summary>
    public required int? CannonWalk { get; init; }

    /// <summary>
    /// Gets the floor's mean distance from its middle over that of a disc of the same area.
    /// </summary>
    public required float Spread { get; init; }

    /// <summary>
    /// Gets the median length over width of the rooms, or null without rooms.
    /// </summary>
    public required float? RoomProportion { get; init; }

    /// <summary>
    /// Gets the door graph of the rooms of a lab level, or null on other levels.
    /// </summary>
    public required RoomGraph? Graph { get; init; }

    /// <summary>
    /// Gets the pairs of places that share half their decor at one offset.
    /// </summary>
    public required int RepeatedRooms { get; init; }

    /// <summary>
    /// Gets the median share of the tour between fights holding 5% or more of the hit points, or null with fewer than two.
    /// </summary>
    public required float? FightGap { get; init; }

    /// <summary>
    /// Gets how many monsters the level's spawners release for every 1,000 hit points they release, or null when the level has no spawner.
    /// </summary>
    public required float? SpawnedPerThousand { get; init; }

    /// <summary>
    /// Gets how many monsters the level's largest spawner releases, or null when the level has no spawner.
    /// </summary>
    public required float? LargestSpawner { get; init; }

    /// <summary>
    /// Gets dynamite sites on screen more than 0.2 of the tour before the route reaches them.
    /// </summary>
    public required MeasureShare SitesPassedEarly { get; init; }

    /// <summary>
    /// Gets solid floor pieces within 30 px of a doorway.
    /// </summary>
    public required MeasureShare SolidNearDoorways { get; init; }

    /// <summary>
    /// Gets hittable floor pieces within 30 px of a doorway.
    /// </summary>
    public required MeasureShare HittableNearDoorways { get; init; }

    /// <summary>
    /// Gets monitors within 40 px of a doorway.
    /// </summary>
    public required MeasureShare MonitorsNearDoorways { get; init; }

    /// <summary>
    /// Gets solid or hittable floor pieces whose center is closer to another piece than a quarter of their sizes.
    /// </summary>
    public required MeasureShare PiecesInsideOthers { get; init; }

    /// <summary>
    /// Gets loose pickups inside the collision box of a solid or hittable floor piece.
    /// </summary>
    public required MeasureShare PickupsUnderPieces { get; init; }

    /// <summary>
    /// Gets monitors within 25 px of a cabinet or shelves.
    /// </summary>
    public required MeasureShare MonitorsOnTallFurniture { get; init; }

    /// <summary>
    /// Gets monitors standing on another piece.
    /// </summary>
    public required MeasureShare MonitorsOnSupports { get; init; }

    /// <summary>
    /// Gets boxes standing on shelves, a cabinet or a table.
    /// </summary>
    public required MeasureShare BoxesOnFurniture { get; init; }

    /// <summary>
    /// Gets test tubes, lab equipment and computers standing on another piece.
    /// </summary>
    public required MeasureShare DeskTopsOnSupports { get; init; }

    /// <summary>
    /// Gets ammunition placed before a fight.
    /// </summary>
    public required MeasureShare AmmoBeforeFights { get; init; }

    /// <summary>
    /// Gets loose money more than 200 px of walk off the route.
    /// </summary>
    public required MeasureShare MoneyOffRoute { get; init; }

    /// <summary>
    /// Gets health in crates.
    /// </summary>
    public required MeasureShare HealthInCrates { get; init; }

    /// <summary>
    /// Gets ammunition in crates.
    /// </summary>
    public required MeasureShare AmmoInCrates { get; init; }

    /// <summary>
    /// Gets armor in crates.
    /// </summary>
    public required MeasureShare ArmorInCrates { get; init; }

    /// <summary>
    /// Gets the loose pickups per pile, or null without loose pickups.
    /// </summary>
    public required float? PileSize { get; init; }
}
