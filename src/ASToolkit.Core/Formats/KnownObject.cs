namespace ASToolkit.Core.Formats;

/// <summary>
/// Defines the object IDs the level code treats specially. Any other ID converts to it with a cast.
/// </summary>
public enum KnownObject
{
    /// <summary>The cursor sprite.</summary>
    Cursor = 1,

    /// <summary>The menu text label.</summary>
    MenuLabel = 3,

    /// <summary>An editor leftover without a definition that only tmp.map places.</summary>
    UndefinedObject7 = 7,

    /// <summary>An editor leftover without a definition that only tmp.map places.</summary>
    UndefinedObject8 = 8,

    /// <summary>The player start marker.</summary>
    PlayerStart = 9,

    /// <summary>The green slug monster.</summary>
    Slug = 40,

    /// <summary>The yellow slug monster.</summary>
    YellowSlug = 43,

    /// <summary>The red slug monster.</summary>
    RedSlug = 46,

    /// <summary>The green monster of kind 1.</summary>
    Monster1 = 50,

    /// <summary>The yellow monster of kind 1.</summary>
    YellowMonster1 = 53,

    /// <summary>The red monster of kind 1.</summary>
    RedMonster1 = 56,

    /// <summary>The green monster of kind 2.</summary>
    Monster2 = 60,

    /// <summary>The yellow monster of kind 2.</summary>
    YellowMonster2 = 63,

    /// <summary>The red monster of kind 2.</summary>
    RedMonster2 = 66,

    /// <summary>The green armed monster.</summary>
    WeaponMonster = 70,

    /// <summary>The yellow armed monster.</summary>
    YellowWeaponMonster = 73,

    /// <summary>The red armed monster.</summary>
    RedWeaponMonster = 76,

    /// <summary>The green tank monster.</summary>
    TankMonster = 80,

    /// <summary>The yellow tank monster.</summary>
    YellowTankMonster = 83,

    /// <summary>The red tank monster.</summary>
    RedTankMonster = 86,

    /// <summary>The green armed tank monster.</summary>
    TankWeaponMonster = 90,

    /// <summary>The yellow armed tank monster.</summary>
    YellowTankWeaponMonster = 93,

    /// <summary>The red tank-weapon monster.</summary>
    RedTankWeaponMonster = 96,

    /// <summary>The dried red blood pool.</summary>
    BloodPool = 110,

    /// <summary>The flying gibs.</summary>
    FlyingGibs = 151,

    /// <summary>The big explosion.</summary>
    BigExplosion = 157,

    /// <summary>The electric fence.</summary>
    Fence = 164,

    /// <summary>The spark of an explosion.</summary>
    ExplosionSpark = 174,

    /// <summary>The dynamite site marker.</summary>
    DynamiteSite = 179,

    /// <summary>The marker that points at a game object.</summary>
    ObjectMarker = 180,

    /// <summary>The small health pickup.</summary>
    Health = 210,

    /// <summary>The big health pickup.</summary>
    BigHealth = 211,

    /// <summary>The mega health pickup.</summary>
    MegaHealth = 212,

    /// <summary>The green armor pickup.</summary>
    GreenArmor = 204,

    /// <summary>The yellow armor pickup.</summary>
    YellowArmor = 205,

    /// <summary>The red armor pickup.</summary>
    RedArmor = 206,

    /// <summary>The invulnerability pickup.</summary>
    Invulnerability = 207,

    /// <summary>The rescue medkit pickup.</summary>
    Medkit = 230,

    /// <summary>The drone pickup.</summary>
    Drone = 231,

    /// <summary>The game speed-up pickup.</summary>
    SpeedUp = 232,

    /// <summary>The game slow-down pickup.</summary>
    SlowDown = 233,

    /// <summary>The monster freeze pickup.</summary>
    FreezeMonsters = 234,

    /// <summary>The extra life pickup.</summary>
    ExtraLife = 235,

    /// <summary>The bomb pickup.</summary>
    Bomb = 236,

    /// <summary>The second bomb pickup.</summary>
    SecondBomb = 237,

    /// <summary>The adrenaline pickup.</summary>
    Adrenaline = 238,

    /// <summary>The money pickup.</summary>
    Money = 241,

    /// <summary>The life implant pickup.</summary>
    LifeImplant = 242,

    /// <summary>The strength implant pickup.</summary>
    StrengthImplant = 243,

    /// <summary>The speed implant pickup.</summary>
    SpeedImplant = 244,

    /// <summary>The accuracy implant pickup.</summary>
    AccuracyImplant = 245,

    /// <summary>The flashlight pickup.</summary>
    Flashlight = 246,

    /// <summary>The night vision goggles pickup.</summary>
    NightVision = 247,

    /// <summary>The shotgun pickup.</summary>
    Shotgun = 262,

    /// <summary>The grenade launcher pickup.</summary>
    GrenadeLauncher = 263,

    /// <summary>The minigun pickup.</summary>
    Minigun = 264,

    /// <summary>The rocket launcher pickup.</summary>
    RocketLauncher = 265,

    /// <summary>The freeze rifle pickup.</summary>
    FreezeRifle = 266,

    /// <summary>The plasma rifle pickup.</summary>
    PlasmaRifle = 267,

    /// <summary>The flame thrower pickup.</summary>
    FlameThrower = 268,

    /// <summary>The magma minigun pickup.</summary>
    MagmaMinigun = 269,

    /// <summary>The dynamite pickup.</summary>
    Dynamite = 301,

    /// <summary>The shotgun ammo pickup.</summary>
    ShotgunAmmo = 302,

    /// <summary>The grenade launcher ammo pickup.</summary>
    GrenadeLauncherAmmo = 303,

    /// <summary>The minigun ammo pickup.</summary>
    MinigunAmmo = 304,

    /// <summary>The rocket launcher ammo pickup.</summary>
    RocketLauncherAmmo = 305,

    /// <summary>The freeze rifle ammo pickup.</summary>
    FreezeRifleAmmo = 306,

    /// <summary>The plasma rifle ammo pickup.</summary>
    PlasmaRifleAmmo = 307,

    /// <summary>The flame thrower ammo pickup.</summary>
    FlameThrowerAmmo = 308,

    /// <summary>The magma minigun ammo pickup.</summary>
    MagmaMinigunAmmo = 309,

    /// <summary>The mounted cannon the player can man.</summary>
    MountedCannon = 357,

    /// <summary>The storage shelves.</summary>
    Shelves = 403,

    /// <summary>The table.</summary>
    Table = 404,

    /// <summary>The cabinet.</summary>
    Cabinet = 413,

    /// <summary>The military base generator.</summary>
    Generator = 415,

    /// <summary>The destroyed military base generator.</summary>
    GeneratorDead = 416,

    /// <summary>The breaker.</summary>
    Breaker = 418,

    /// <summary>The breakable wooden crate.</summary>
    Crate = 419,

    /// <summary>The keypad of a door that opens.</summary>
    KeypadOn = 423,

    /// <summary>The keypad of a locked door.</summary>
    KeypadOff = 424,

    /// <summary>The broken keypad of a jammed door.</summary>
    BrokenKeypad = 425,

    /// <summary>The electric transformer.</summary>
    Transformer = 429,

    /// <summary>The soldier corpse.</summary>
    SoldierCorpse = 430,

    /// <summary>The scientist corpse.</summary>
    ScientistCorpse = 431,

    /// <summary>The monster teleport pad.</summary>
    TeleportPad = 450,

    /// <summary>The teleport pad that spawns no monsters.</summary>
    SilentTeleportPad = 453,

    /// <summary>The basement floor tile.</summary>
    BasementFloor = 500,

    /// <summary>The earth tile.</summary>
    Earth = 501,

    /// <summary>The second basement floor tile.</summary>
    BasementFloorAlt = 502,

    /// <summary>The military base floor tile.</summary>
    MilitaryFloor = 503,

    /// <summary>The grass tile.</summary>
    Grass = 505,

    /// <summary>The volumetric grass tile.</summary>
    VolumetricGrass = 506,

    /// <summary>The flat grass tile with alpha.</summary>
    FlatGrass = 507,

    /// <summary>The female player's start marker.</summary>
    PlayerStartGirl = 549,

    /// <summary>The lab wall that runs along '/'.</summary>
    LabWallSlash = 601,

    /// <summary>The lab wall that runs along '\'.</summary>
    LabWallBack = 602,

    /// <summary>The lab wall beam.</summary>
    LabBeam = 604,

    /// <summary>The small door.</summary>
    SmallDoor = 605,

    /// <summary>The wall piece for a small door.</summary>
    SmallDoorWall = 606,

    /// <summary>The double door.</summary>
    DoubleDoor = 607,

    /// <summary>The wall piece for a double door.</summary>
    DoubleDoorWall = 608,

    /// <summary>The breakable lab wall.</summary>
    BreakableWall = 609,

    /// <summary>The broken wall a human fits through.</summary>
    BrokenWall = 610,

    /// <summary>The built-in double door.</summary>
    DoubleDoorFrame = 611,

    /// <summary>The wall piece with a hole for a small monster.</summary>
    SmallMonsterHole = 612,

    /// <summary>The pile of rubble from a collapse.</summary>
    RubblePile = 615,

    /// <summary>The wall piece with a hole for a big monster.</summary>
    BigMonsterHole = 616,

    /// <summary>The built-in small door.</summary>
    SmallDoorFrame = 617,

    /// <summary>The wall piece monsters appear from, along '/'.</summary>
    SpawnWallSlash = 618,

    /// <summary>The wall piece monsters appear from, along '\'.</summary>
    SpawnWallBack = 619,

    /// <summary>The second lab wall style along '/'.</summary>
    LabWall2Slash = 620,

    /// <summary>The second lab wall style along '\'.</summary>
    LabWall2Back = 621,

    /// <summary>The second style wall beam.</summary>
    LabBeam2 = 622,

    /// <summary>The second style wall piece for a small door.</summary>
    SmallDoorWall2 = 623,

    /// <summary>The second style wall piece for a double door.</summary>
    DoubleDoorWall2 = 624,

    /// <summary>The second style breakable wall.</summary>
    BreakableWall2 = 625,

    /// <summary>The second style broken wall.</summary>
    BrokenWall2 = 626,

    /// <summary>The invisible dummy that blocks like a solid object.</summary>
    ZDummy = 631,

    /// <summary>The military wall along '\'.</summary>
    MilitaryWallBack = 650,

    /// <summary>The military wall along '/'.</summary>
    MilitaryWallSlash = 651,

    /// <summary>The military wall corner.</summary>
    MilitaryCorner = 652,

    /// <summary>The electric fence emitter.</summary>
    FenceEmitter = 653,

    /// <summary>The military wall piece for a door.</summary>
    MilitaryDoorWall = 654,

    /// <summary>The lattice wall along '/'.</summary>
    LatticeWall = 660,

    /// <summary>The lattice fence.</summary>
    LatticeFence = 661,

    /// <summary>The torn lattice wall.</summary>
    TornLattice = 662,

    /// <summary>The windows.</summary>
    Windows = 664,

    /// <summary>The boards.</summary>
    Boards = 666,

    /// <summary>The books.</summary>
    Books = 667,

    /// <summary>The toilet.</summary>
    Toilet = 670,

    /// <summary>The wooden door.</summary>
    WoodenDoor = 671,

    /// <summary>The menu button.</summary>
    MenuButton = 705,

    /// <summary>The first endless monster generator.</summary>
    FirstEndlessGenerator = 800,

    /// <summary>The last endless monster generator.</summary>
    LastEndlessGenerator = 814,

    /// <summary>The boss of level 1.</summary>
    Boss1 = 850,

    /// <summary>The boss of level 2.</summary>
    Boss2 = 853,

    /// <summary>The boss of level 3.</summary>
    Boss3 = 856,

    /// <summary>The boss of level 4.</summary>
    Boss4 = 860,

    /// <summary>The tank monster used as a boss.</summary>
    TankBoss = 863,

    /// <summary>The red tank-weapon monster used as a boss.</summary>
    RedTankWeaponBoss = 866,

    /// <summary>The more powerful boss of level 3.</summary>
    PowerfulBoss3 = 867,

    /// <summary>The flying monster that spawns.</summary>
    Bat = 880,

    /// <summary>The diver monster.</summary>
    Diver = 885,

    /// <summary>The diver with the boss weapon.</summary>
    DiverBoss = 890,

    /// <summary>The generator sound source.</summary>
    GeneratorSound = 903,

    /// <summary>The scripted monster spawner.</summary>
    Spawner = 908,

    /// <summary>The dummy that shakes the screen and plays a sound.</summary>
    ShakeDummy = 909,

    /// <summary>The dummy that spawns objects.</summary>
    SpawnDummy = 911,

    /// <summary>The shoot here marker.</summary>
    ShootHere = 912,

    /// <summary>The dummy that spawns the shoot here marker.</summary>
    ShootHereSpawner = 913,

    /// <summary>The put dynamite here marker.</summary>
    PutDynamiteHere = 914,

    /// <summary>The dummy that spawns the put dynamite here marker.</summary>
    PutDynamiteHereSpawner = 915,

    /// <summary>The large dried red blood pool.</summary>
    LargeBloodPool = 916,

    /// <summary>The monster of the last map.</summary>
    FinalMonster = 925,

    /// <summary>The soft black cover that hides ground outside the rooms in Alien Shooter 2.</summary>
    SoftCover = 1127,

    /// <summary>The solid black block that hides ground outside the rooms in Alien Shooter 2.</summary>
    BlackBlock = 1710,

    /// <summary>The dummy that shows the sprite of the soft cover over everything below it in Alien Shooter 2.</summary>
    SoftCoverDummy = 1711,
}
