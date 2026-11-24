namespace ASToolkit.Formats;

/// <summary>
/// Defines the object IDs the level code treats specially. Any other ID converts to it with a cast.
/// </summary>
public enum KnownObject
{
    /// <summary>No object.</summary>
    None = -1,

    /// <summary>The cursor sprite.</summary>
    Cursor = 1,

    /// <summary>The font sprite.</summary>
    Font = 2,

    /// <summary>The menu text label.</summary>
    MenuLabel = 3,

    /// <summary>An editor leftover without an object record that only tmp.map places.</summary>
    UndefinedObject7 = 7,

    /// <summary>An editor leftover without an object record that only tmp.map places.</summary>
    UndefinedObject8 = 8,

    /// <summary>The player start marker.</summary>
    PlayerStart = 9,

    /// <summary>The player's torso with the secret weapon.</summary>
    SecretWeaponTorso = 10,

    /// <summary>The player's torso with pistols.</summary>
    PistolsTorso = 11,

    /// <summary>The green slug monster.</summary>
    Slug = 40,

    /// <summary>The slug corpse.</summary>
    SlugCorpse = 42,

    /// <summary>The yellow slug monster.</summary>
    YellowSlug = 43,

    /// <summary>The red slug monster.</summary>
    RedSlug = 46,

    /// <summary>The frozen slug.</summary>
    FrozenSlug = 49,

    /// <summary>The green monster of kind 1.</summary>
    Monster1 = 50,

    /// <summary>The death animation of monster 1.</summary>
    Monster1Death = 51,

    /// <summary>The first monster's corpse.</summary>
    Monster1Corpse = 52,

    /// <summary>The yellow monster of kind 1.</summary>
    YellowMonster1 = 53,

    /// <summary>The yellow first monster's corpse.</summary>
    YellowMonster1Corpse = 55,

    /// <summary>The red monster of kind 1.</summary>
    RedMonster1 = 56,

    /// <summary>The corpse of the red monster of kind 1.</summary>
    RedMonster1Corpse = 58,

    /// <summary>The frozen monster of kind 1.</summary>
    FrozenMonster1 = 59,

    /// <summary>The green monster of kind 2.</summary>
    Monster2 = 60,

    /// <summary>The corpse of the monster of kind 2.</summary>
    Monster2Corpse = 62,

    /// <summary>The yellow monster of kind 2.</summary>
    YellowMonster2 = 63,

    /// <summary>The red monster of kind 2.</summary>
    RedMonster2 = 66,

    /// <summary>The corpse of the red monster of kind 2.</summary>
    RedMonster2Corpse = 68,

    /// <summary>The frozen monster of kind 2.</summary>
    FrozenMonster2 = 69,

    /// <summary>The green armed monster.</summary>
    WeaponMonster = 70,

    /// <summary>The yellow armed monster.</summary>
    YellowWeaponMonster = 73,

    /// <summary>The red armed monster.</summary>
    RedWeaponMonster = 76,

    /// <summary>The frozen weapon monster.</summary>
    FrozenWeaponMonster = 79,

    /// <summary>The green tank monster.</summary>
    TankMonster = 80,

    /// <summary>The yellow tank monster.</summary>
    YellowTankMonster = 83,

    /// <summary>The red tank monster.</summary>
    RedTankMonster = 86,

    /// <summary>The frozen tank monster.</summary>
    FrozenTankMonster = 89,

    /// <summary>The green armed tank monster.</summary>
    TankWeaponMonster = 90,

    /// <summary>The yellow armed tank monster.</summary>
    YellowTankWeaponMonster = 93,

    /// <summary>The red tank-weapon monster.</summary>
    RedTankWeaponMonster = 96,

    /// <summary>The frozen armed tank monster.</summary>
    FrozenTankWeaponMonster = 99,

    /// <summary>The shotgun pellet.</summary>
    ShotgunPellet = 100,

    /// <summary>The casing ejected from a weapon.</summary>
    FlyingCasing = 102,

    /// <summary>The weapon casing lying on the floor.</summary>
    LyingCasing = 103,

    /// <summary>The muzzle flash of a pistol shot.</summary>
    PistolMuzzleFlash = 104,

    /// <summary>The effect of a bullet hitting a wall.</summary>
    BulletWallImpact = 105,

    /// <summary>The small shadow.</summary>
    SmallShadow = 108,

    /// <summary>The heavy rocket of armed monsters.</summary>
    HeavyMonsterRocket = 109,

    /// <summary>The dried red blood pool.</summary>
    BloodPool = 110,

    /// <summary>The plasma bullet.</summary>
    PlasmaBullet = 112,

    /// <summary>The rocket projectile.</summary>
    Rocket = 113,

    /// <summary>The small dried blood pool.</summary>
    SmallBloodPool = 120,

    /// <summary>The glow under health pickups.</summary>
    HealthGlow = 125,

    /// <summary>The muzzle flash of a shotgun shot.</summary>
    ShotgunMuzzleFlash = 126,

    /// <summary>The casing ejected from a shotgun.</summary>
    FlyingShotgunCasing = 127,

    /// <summary>The shotgun casing lying on the floor.</summary>
    LyingShotgunCasing = 128,

    /// <summary>The magma bullet.</summary>
    MagmaBullet = 129,

    /// <summary>The wall-mounted objects.</summary>
    WallObjects = 130,

    /// <summary>The wall inscriptions.</summary>
    WallInscriptions = 131,

    /// <summary>The small blast mark.</summary>
    BlastMark = 133,

    /// <summary>The big computer.</summary>
    BigComputer = 135,

    /// <summary>The electric panel.</summary>
    ElectricPanel = 136,

    /// <summary>The glow under ammunition pickups.</summary>
    AmmoGlow = 139,

    /// <summary>The small built-in shadow.</summary>
    EmbeddedShadow = 144,

    /// <summary>The freeze rifle bullet.</summary>
    FreezeBullet = 145,

    /// <summary>The flying gibs.</summary>
    FlyingGibs = 151,

    /// <summary>The gibs lying on the floor.</summary>
    LyingGibs = 152,

    /// <summary>The vertical dried blood pool.</summary>
    VerticalBloodPool = 154,

    /// <summary>The minigun bullet.</summary>
    MinigunBullet = 156,

    /// <summary>The big explosion.</summary>
    BigExplosion = 157,

    /// <summary>The computer wreckage lying on the floor.</summary>
    ComputerWreck = 161,

    /// <summary>The electric fence.</summary>
    Fence = 164,

    /// <summary>The pistol bullet.</summary>
    PistolBullet = 168,

    /// <summary>The spark of an explosion.</summary>
    ExplosionSpark = 174,

    /// <summary>The dynamite site marker.</summary>
    DynamiteSite = 179,

    /// <summary>The marker that points at a game object.</summary>
    ObjectMarker = 180,

    /// <summary>The effect of using a rescue medkit.</summary>
    RescueMedkitEffect = 181,

    /// <summary>The dynamite charge before its explosion.</summary>
    ArmedDynamite = 184,

    /// <summary>The flying green gibs.</summary>
    FlyingGreenGibs = 197,

    /// <summary>The green gibs lying on the floor.</summary>
    LyingGreenGibs = 198,

    /// <summary>The green body armor worn by the player.</summary>
    WornGreenArmor = 200,

    /// <summary>The red body armor worn by the player.</summary>
    WornRedArmor = 202,

    /// <summary>The invulnerability object linked to the player.</summary>
    InvulnerabilityEffect = 203,

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

    /// <summary>The drone that follows the player.</summary>
    FlyingDrone = 290,

    /// <summary>The turret of the drone.</summary>
    DroneTurret = 291,

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

    /// <summary>The second weapon of the yellow tank alien, which carries its turret.</summary>
    Monster4Weapon2 = 332,

    /// <summary>The third weapon of the red tank alien, which carries its turret.</summary>
    Monster4Weapon3 = 333,

    /// <summary>The first weapon of the tank alien.</summary>
    Monster4Weapon1 = 334,

    /// <summary>The mounted cannon with the player in it.</summary>
    MannedCannon = 350,

    /// <summary>The turret of the manned cannon.</summary>
    MannedCannonTurret = 352,

    /// <summary>The shell of the mounted cannon.</summary>
    CannonShell = 356,

    /// <summary>The mounted cannon the player can man.</summary>
    MountedCannon = 357,

    /// <summary>The red container.</summary>
    Container = 400,

    /// <summary>The barrel.</summary>
    Barrel = 401,

    /// <summary>The cylinder.</summary>
    Cylinder = 402,

    /// <summary>The storage shelves.</summary>
    Shelves = 403,

    /// <summary>The table.</summary>
    Table = 404,

    /// <summary>The monitor.</summary>
    Monitor = 405,

    /// <summary>The dead monitor.</summary>
    MonitorDead = 406,

    /// <summary>The chair.</summary>
    Chair = 407,

    /// <summary>The scattered paper.</summary>
    ScatteredPaper = 408,

    /// <summary>The drinking water cylinder.</summary>
    WaterCylinder = 409,

    /// <summary>The computer.</summary>
    Computer = 410,

    /// <summary>The dead computer.</summary>
    ComputerDead = 411,

    /// <summary>The plant.</summary>
    Plant = 412,

    /// <summary>The cabinet.</summary>
    Cabinet = 413,

    /// <summary>The experiment flasks.</summary>
    ExperimentFlasks = 414,

    /// <summary>The military base generator.</summary>
    Generator = 415,

    /// <summary>The destroyed military base generator.</summary>
    GeneratorDead = 416,

    /// <summary>The electric breaker switched on.</summary>
    BreakerOn = 417,

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

    /// <summary>The test tubes.</summary>
    TestTubes = 426,

    /// <summary>The lab equipment.</summary>
    LabEquipment = 427,

    /// <summary>The boxes.</summary>
    Boxes = 428,

    /// <summary>The electric transformer.</summary>
    Transformer = 429,

    /// <summary>The soldier corpse.</summary>
    SoldierCorpse = 430,

    /// <summary>The scientist corpse.</summary>
    ScientistCorpse = 431,

    /// <summary>The radioactive barrel.</summary>
    RadioactiveBarrel = 435,

    /// <summary>The radioactive flask.</summary>
    RadioactiveFlask = 436,

    /// <summary>The missile on the military base.</summary>
    Missile = 437,

    /// <summary>The cargo truck.</summary>
    Truck = 440,

    /// <summary>The second container.</summary>
    Container2 = 441,

    /// <summary>The second container's other variant.</summary>
    Container2Alt = 442,

    /// <summary>The large bio-container.</summary>
    BioContainer = 443,

    /// <summary>The monster teleport pad.</summary>
    TeleportPad = 450,

    /// <summary>The dead teleport pad for small monsters.</summary>
    DeadSmallMonsterTeleportPad = 451,

    /// <summary>The teleport pad that doesn't spawn any monsters.</summary>
    SilentTeleportPad = 453,

    /// <summary>The basement floor tile.</summary>
    BasementFloor = 500,

    /// <summary>The earth tile.</summary>
    Earth = 501,

    /// <summary>The second basement floor tile.</summary>
    BasementFloorAlt = 502,

    /// <summary>The military base floor tile.</summary>
    MilitaryBaseFloor = 503,

    /// <summary>The grass tile.</summary>
    Grass = 505,

    /// <summary>The volumetric grass tile.</summary>
    VolumetricGrass = 506,

    /// <summary>The flat grass tile with alpha.</summary>
    FlatGrass = 507,

    /// <summary>The female player legs, placed as a player start.</summary>
    PlayerStartFemaleLegs = 549,

    /// <summary>The basement wall (wall 1) that runs along '/'.</summary>
    BasementWallSlash = 601,

    /// <summary>The basement wall (wall 1) that runs along '\'.</summary>
    BasementWallBack = 602,

    /// <summary>The basement wall beam.</summary>
    BasementBeam = 604,

    /// <summary>The small door.</summary>
    SmallDoor = 605,

    /// <summary>The wall piece for a small door.</summary>
    SmallDoorWall = 606,

    /// <summary>The double door.</summary>
    DoubleDoor = 607,

    /// <summary>The wall piece for a double door.</summary>
    DoubleDoorWall = 608,

    /// <summary>The breakable basement wall.</summary>
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

    /// <summary>The second basement wall (wall 2) along '/'.</summary>
    BasementWall2Slash = 620,

    /// <summary>The second basement wall (wall 2) along '\'.</summary>
    BasementWall2Back = 621,

    /// <summary>The second style wall beam.</summary>
    BasementBeam2 = 622,

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

    /// <summary>The military base wall along '\'.</summary>
    MilitaryBaseWallBack = 650,

    /// <summary>The military base wall along '/'.</summary>
    MilitaryBaseWallSlash = 651,

    /// <summary>The military base corner.</summary>
    MilitaryBaseCorner = 652,

    /// <summary>The electric fence emitter.</summary>
    FenceEmitter = 653,

    /// <summary>The military base wall piece for a door.</summary>
    MilitaryBaseDoorWall = 654,

    /// <summary>The lattice wall along '/'.</summary>
    LatticeWall = 660,

    /// <summary>The lattice fence.</summary>
    LatticeFence = 661,

    /// <summary>The torn lattice wall.</summary>
    TornLattice = 662,

    /// <summary>The street lamp.</summary>
    StreetLamp = 663,

    /// <summary>The windows.</summary>
    Windows = 664,

    /// <summary>The yard with a bench and swings.</summary>
    Playground = 665,

    /// <summary>The boards.</summary>
    Boards = 666,

    /// <summary>The books.</summary>
    Books = 667,

    /// <summary>The gym equipment.</summary>
    GymEquipment = 668,

    /// <summary>The trees and bushes.</summary>
    Trees = 669,

    /// <summary>The toilet.</summary>
    Toilet = 670,

    /// <summary>The wooden door.</summary>
    WoodenDoor = 671,

    /// <summary>The petrol generator.</summary>
    PetrolGenerator = 672,

    /// <summary>The telephone.</summary>
    Telephone = 673,

    /// <summary>The record player.</summary>
    RecordPlayer = 674,

    /// <summary>The trash can.</summary>
    TrashCan = 675,

    /// <summary>The television.</summary>
    Television = 676,

    /// <summary>The new chair.</summary>
    NewChair = 678,

    /// <summary>The menu button.</summary>
    MenuButton = 705,

    /// <summary>The electric ball.</summary>
    ElectricBall = 751,

    /// <summary>The red blood flying off a monster.</summary>
    RedBloodSpray = 756,

    /// <summary>The glow under bonuses.</summary>
    BonusGlow = 759,

    /// <summary>The effect above the player when a medkit is collected.</summary>
    HealingEffect = 760,

    /// <summary>The death effect of monster 70.</summary>
    WeaponMonsterDeath = 771,

    /// <summary>The flying rock debris.</summary>
    RockDebris = 782,

    /// <summary>The rock debris lying on the floor.</summary>
    LyingRockDebris = 783,

    /// <summary>The big outdoor light spot.</summary>
    BigOutdoorLight = 784,

    /// <summary>The volumetric lamp light.</summary>
    VolumetricLampLight = 785,

    /// <summary>The endless generator of green monsters of kind 1 (<see cref="Monster1"/>).</summary>
    FirstEndlessGenerator = 800,

    /// <summary>The endless generator of yellow monsters of kind 1 (<see cref="YellowMonster1"/>).</summary>
    YellowEndlessGenerator1 = 801,

    /// <summary>The endless generator of red monsters of kind 1 (<see cref="RedMonster1"/>).</summary>
    RedEndlessGenerator1 = 802,

    /// <summary>The endless generator of green monsters of kind 2 (<see cref="Monster2"/>).</summary>
    EndlessGenerator2 = 803,

    /// <summary>The endless generator of yellow monsters of kind 2 (<see cref="YellowMonster2"/>).</summary>
    YellowEndlessGenerator2 = 804,

    /// <summary>The endless generator of red monsters of kind 2 (<see cref="RedMonster2"/>).</summary>
    RedEndlessGenerator2 = 805,

    /// <summary>The endless generator of green armed monsters (<see cref="WeaponMonster"/>).</summary>
    EndlessGenerator3 = 806,

    /// <summary>The endless generator of yellow armed monsters (<see cref="YellowWeaponMonster"/>).</summary>
    YellowEndlessGenerator3 = 807,

    /// <summary>The endless generator of red armed monsters (<see cref="RedWeaponMonster"/>).</summary>
    RedEndlessGenerator3 = 808,

    /// <summary>The endless generator of green armed tank monsters (<see cref="TankWeaponMonster"/>).</summary>
    EndlessGenerator5 = 809,

    /// <summary>The endless generator of yellow armed tank monsters (<see cref="YellowTankWeaponMonster"/>).</summary>
    YellowEndlessGenerator5 = 810,

    /// <summary>The endless generator of red armed tank monsters (<see cref="RedTankWeaponMonster"/>).</summary>
    RedEndlessGenerator5 = 811,

    /// <summary>The endless generator of green slug monsters (<see cref="Slug"/>).</summary>
    EndlessGenerator6 = 812,

    /// <summary>The endless generator of yellow slug monsters (<see cref="YellowSlug"/>).</summary>
    YellowEndlessGenerator6 = 813,

    /// <summary>The endless generator of red slug monsters (<see cref="RedSlug"/>).</summary>
    LastEndlessGenerator = 814,

    /// <summary>The boss of level 1.</summary>
    Boss1 = 850,

    /// <summary>The first boss's death remains.</summary>
    Boss1Death = 851,

    /// <summary>The corpse of the level 1 boss.</summary>
    Boss1Corpse = 852,

    /// <summary>The boss of level 2.</summary>
    Boss2 = 853,

    /// <summary>The corpse of the level 2 boss.</summary>
    Boss2Corpse = 855,

    /// <summary>The boss of level 3.</summary>
    Boss3 = 856,

    /// <summary>The boss of level 4.</summary>
    Boss4 = 860,

    /// <summary>The corpse of the level 4 boss.</summary>
    Boss4Corpse = 862,

    /// <summary>The blue tank monster.</summary>
    BlueTankMonster = 863,

    /// <summary>The corpse of the blue tank monster.</summary>
    BlueTankMonsterCorpse = 865,

    /// <summary>The blue tank-weapon monster.</summary>
    BlueTankWeaponMonster = 866,

    /// <summary>The more powerful boss of level 3.</summary>
    PowerfulBoss3 = 867,

    /// <summary>The flyer, a flying monster that spawns.</summary>
    Flyer = 880,

    /// <summary>The larger shadow.</summary>
    LargerShadow = 883,

    /// <summary>The diver monster.</summary>
    Diver = 885,

    /// <summary>The diver corpse.</summary>
    DiverCorpse = 887,

    /// <summary>The diver with the boss weapon.</summary>
    DiverBoss = 890,

    /// <summary>The corpse of the diver with a weapon.</summary>
    DiverBossCorpse = 892,

    /// <summary>The generator sound source.</summary>
    GeneratorSound = 903,

    /// <summary>The damage object of a flamethrower attack.</summary>
    FlamethrowerAttack = 904,

    /// <summary>The machine-gun turret of the yellow and red tank aliens.</summary>
    MonsterMachineGunDummy = 905,

    /// <summary>The scripted monster spawner.</summary>
    Spawner = 908,

    /// <summary>The dummy that shakes the screen and plays a sound.</summary>
    ShakeDummy = 909,

    /// <summary>The violent death effect of a small monster.</summary>
    SmallMonsterViolentDeath = 910,

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

    /// <summary>The first monster's corpse without blood.</summary>
    Monster1CorpseNoBlood = 917,

    /// <summary>The dried red and green blood pool.</summary>
    GreenBloodPool = 918,

    /// <summary>The monster of the last map.</summary>
    FinalMonster = 925,

    /// <summary>The neural bullet.</summary>
    NeuralBullet = 929,

    /// <summary>The soft black cover that hides ground outside the rooms in Alien Shooter 2.</summary>
    SoftCover = 1127,

    /// <summary>The player legs that the Alien Shooter 2 levels place and the start script takes as the player.</summary>
    SequelPlayer = 250,

    /// <summary>The dummy whose death moves the point where the player comes back in Alien Shooter 2.</summary>
    RespawnPost = 1359,

    /// <summary>The solid black block that hides ground outside the rooms in Alien Shooter 2.</summary>
    BlackBlock = 1710,

    /// <summary>The dummy that shows the sprite of the soft cover over everything below it in Alien Shooter 2.</summary>
    SoftCoverDummy = 1711
}
