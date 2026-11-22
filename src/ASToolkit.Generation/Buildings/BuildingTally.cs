namespace ASToolkit.Generation.Buildings;

// Counted under the snake-case name of the outcome, with the wing kind appended when there is one
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
