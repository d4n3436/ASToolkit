namespace ASToolkit.Engine;

internal enum MapEvent
{
    Main = 0,
    TrainNotAmmo = 1,
    TrainNotPower = 2,
    TrainDamage = 3,
    TrainCreated = 4,
    TrainSplit = 5,
    TrainDestroy = 6,
    TrainDestroyPower = 7,
    TrainArrive = 8,
    TrainNotArrive = 9,
    TrainAttacked = 10,
    DepoDestroy = 11,
    DepoBirth = 12,
    DepoAttacked = 13,
    DepoFree = 14,
    BuildingCapture = 15,
    MasterDestroy = 16,
    Unnamed = 17,
    SuperWeaponWounded = 18,
    MineBlast = 19,
    MineRemove = 20,
    EnemyLinked = 21,
    TrainClash = 22,
    UnitCreated = 23,
    UnitDestroy = 24
}
