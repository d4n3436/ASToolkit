namespace ASToolkit.Core.Generation.Buildings;

// Specifies an outcome a building grammar counts, written as its snake-case name with a wing kind appended when one is given
internal enum BuildingTally
{
    BuildingUnlaid,
    RoomsPastWings,
    Wing,
    Short,
    Fail,
    WingUnsolved,
    WingCollides,
    GatedRetry,
    RoomsMoved,
    DarkBlockUnplaced,
    OfficePod,
    OfficePodUnplaced,
    OfficeRingAsked,
    OfficeClumpAsked,
    OfficeRingBuilt,
    OfficeClumpBuilt,
    OfficeFoldUnsolved,
    OfficeClumpUnsolved,
    OfficeBlockRedraws,
    DepotWing,
    DepotWingUndrawn,
    DepotWingCollides,
    DepotWingUnplaced,
    ResearchWing,
    ResearchUndrawn,
    ResearchCollides,
    PodBoxes,
    SecretRoomUnplaced,
    ClosetUnplaced,
    WarehouseDraws,
    WarehouseUnsolved
}
