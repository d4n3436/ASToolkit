using System.Buffers.Binary;
using System.Collections.Frozen;
using ASToolkit.Core.Formats;
using static ASToolkit.Core.Formats.KnownObject;

namespace ASToolkit.Core.Levels;

/// <summary>
/// Provides the groups of object IDs and the checks that go with <see cref="KnownObject"/>.
/// </summary>
public static class KnownObjectExtensions
{
    private static readonly FrozenSet<KnownObject> MonstersSet = new[]
    {
        Slug, YellowSlug, RedSlug, Monster1, YellowMonster1, RedMonster1, Monster2, YellowMonster2, RedMonster2, WeaponMonster, YellowWeaponMonster,
        RedWeaponMonster, TankMonster, YellowTankMonster, RedTankMonster, TankWeaponMonster, YellowTankWeaponMonster, RedTankWeaponMonster
    }.ToFrozenSet();

    private static readonly FrozenSet<KnownObject> BossesSet = new[] { Boss1, Boss2, Boss3, Boss4, TankBoss, BlueTankWeaponBoss, PowerfulBoss3, Bat, Diver, DiverBoss, FinalMonster }.ToFrozenSet();

    private static readonly FrozenSet<KnownObject> ObjectiveMonstersSet = MonstersSet.Append(Bat).Append(Diver).ToFrozenSet();

    private static readonly FrozenSet<KnownObject> FloorsSet = new[] { BasementFloor, Earth, BasementFloorAlt, MilitaryBaseFloor, Grass, VolumetricGrass, FlatGrass }.ToFrozenSet();

    private static readonly FrozenSet<KnownObject> BasementSlashSet = new[]
    {
        BasementWallSlash, BasementWall2Slash, SmallDoorWall, SmallDoorWall2, DoubleDoorWall, DoubleDoorWall2,
        BreakableWall, BreakableWall2, SpawnWallSlash, BrokenWall, BrokenWall2
    }.ToFrozenSet();

    private static readonly FrozenSet<KnownObject> BasementBackSet = new[] { BasementWallBack, SpawnWallBack, BasementWall2Back, SmallMonsterHole, BigMonsterHole }.ToFrozenSet();

    private static readonly FrozenSet<KnownObject> BasementDoubleSet = new[] { DoubleDoor, DoubleDoorWall, DoubleDoorFrame, DoubleDoorWall2 }.ToFrozenSet();

    private static readonly FrozenSet<KnownObject> BasementWallsSet = new[]
    {
        BasementWallSlash, BasementWallBack, BasementBeam, SmallDoor, SmallDoorWall, DoubleDoor, DoubleDoorWall, BreakableWall, BrokenWall, DoubleDoorFrame, SmallMonsterHole, RubblePile,
        BigMonsterHole, SmallDoorFrame, SpawnWallSlash, SpawnWallBack, BasementWall2Slash, BasementWall2Back, BasementBeam2, SmallDoorWall2, DoubleDoorWall2, BreakableWall2, BrokenWall2
    }.ToFrozenSet();

    private static readonly FrozenSet<KnownObject> ConcreteWallsSet = new[] { MilitaryBaseWallBack, MilitaryBaseWallSlash, MilitaryBaseCorner, FenceEmitter, MilitaryBaseDoorWall }.ToFrozenSet();

    private static readonly FrozenSet<KnownObject> GameplaySet = new[] 
    {
        Cursor, UndefinedObject7, UndefinedObject8, PlayerStart, PlayerStartFemaleLegs, DynamiteSite, ObjectMarker, Fence, Generator, GeneratorDead, Breaker,
        KeypadOn, KeypadOff, MountedCannon, TeleportPad, SilentTeleportPad, GeneratorSound, Spawner, ShakeDummy, SpawnDummy, ShootHere, ShootHereSpawner,
        PutDynamiteHere, PutDynamiteHereSpawner, BigExplosion, ExplosionSpark, FlyingGibs
    }.Concat(Enumerable.Range((int)FirstEndlessGenerator, LastEndlessGenerator - FirstEndlessGenerator + 1).Select(id => (KnownObject)id)).ToFrozenSet();

    private const int EmptyCratePayloadSize = 12;
    private const int CratePayloadSize = 16;

    extension(KnownObject)
    {
        /// <summary>Gets the IDs of the ordinary monsters.</summary>
        public static FrozenSet<KnownObject> Monsters => MonstersSet;

        /// <summary>Gets the IDs of the monsters that can be a level's first objective: the ordinary monsters plus the bat and the diver.</summary>
        public static FrozenSet<KnownObject> ObjectiveMonsters => ObjectiveMonstersSet;

        /// <summary>Gets the IDs of the bosses and special monsters.</summary>
        public static FrozenSet<KnownObject> Bosses => BossesSet;

        /// <summary>Gets the IDs of the floor tiles.</summary>
        public static FrozenSet<KnownObject> Floors => FloorsSet;

        /// <summary>Gets the IDs of the basement wall pieces that run along '/'.</summary>
        public static FrozenSet<KnownObject> BasementSlash => BasementSlashSet;

        /// <summary>Gets the IDs of the basement wall pieces that run along '\'.</summary>
        public static FrozenSet<KnownObject> BasementBack => BasementBackSet;

        /// <summary>Gets the IDs of the double doors and frames, two wall pieces wide.</summary>
        public static FrozenSet<KnownObject> BasementDouble => BasementDoubleSet;

        /// <summary>Gets the IDs of every basement wall, door and post piece.</summary>
        public static FrozenSet<KnownObject> BasementWalls => BasementWallsSet;

        /// <summary>Gets the IDs of every concrete wall piece.</summary>
        public static FrozenSet<KnownObject> ConcreteWalls => ConcreteWallsSet;

        /// <summary>
        /// Returns the payload of an empty crate.
        /// </summary>
        public static byte[] CreateEmptyCratePayload() => new byte[EmptyCratePayloadSize];

        /// <summary>
        /// Returns the payload of a crate holding a pickup.
        /// </summary>
        public static byte[] CreateCratePayload(KnownObject item)
        {
            byte[] payload = new byte[CratePayloadSize];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), (uint)item);
            return payload;
        }

        /// <summary>
        /// Returns the ID of the pickup inside a crate payload, or 0 for an empty crate.
        /// </summary>
        public static KnownObject GetCrateItem(ReadOnlySpan<byte> payload) =>
            payload.Length >= CratePayloadSize && BinaryPrimitives.ReadUInt32LittleEndian(payload[4..]) == 1
                ? (KnownObject)BinaryPrimitives.ReadUInt32LittleEndian(payload[8..])
                : 0;
    }

    extension(KnownObject id)
    {
        /// <summary>Gets a value indicating whether the ID is a pickup.</summary>
        public bool IsPickup => id is >= (KnownObject)200 and <= MagmaMinigun or >= Dynamite and <= MagmaMinigunAmmo;

        /// <summary>Gets the plain monster a yellow or red variant belongs to, the ID itself for any other unit.</summary>
        public KnownObject MonsterFamily => id is >= Slug and <= RedTankWeaponMonster ? (KnownObject)((int)id / 10 * 10) : id;

        /// <summary>Gets a value indicating whether the ID is an endless monster generator.</summary>
        public bool IsEndlessGenerator => id is >= FirstEndlessGenerator and <= LastEndlessGenerator;

        /// <summary>Gets the category of the pickup, or null when the ID is not a pickup.</summary>
        public ItemCategory? PickupCategory => id switch
        {
            Health or BigHealth or MegaHealth or Medkit or ExtraLife => ItemCategory.Health,
            >= GreenArmor and <= Invulnerability => ItemCategory.Armor,
            >= ShotgunAmmo and <= MagmaMinigunAmmo => ItemCategory.Ammo,
            >= Shotgun and <= MagmaMinigun => ItemCategory.Weapon,
            Dynamite => ItemCategory.Dynamite,
            Money => ItemCategory.Money,
            >= LifeImplant and <= AccuracyImplant => ItemCategory.Implant,
            Drone or SpeedUp or SlowDown or FreezeMonsters or Bomb or SecondBomb or Adrenaline or Flashlight or NightVision => ItemCategory.Bonus,
            _ => null
        };

        /// <summary>Gets a value indicating whether the ID is a weapon pickup.</summary>
        public bool IsWeapon => id is >= Shotgun and <= MagmaMinigun;

        /// <summary>Gets a value indicating whether the ID is an ammunition pickup.</summary>
        public bool IsAmmo => id is >= ShotgunAmmo and <= MagmaMinigunAmmo;

        /// <summary>Gets the ammunition pickup of a weapon pickup, or null when the ID is not a weapon.</summary>
        public KnownObject? WeaponAmmo => id.IsWeapon ? id - Shotgun + ShotgunAmmo : null;

        /// <summary>Gets the furniture family of the piece, or null when it belongs to none.</summary>
        public PieceFamily? Family => id switch
        {
            Table => PieceFamily.Table,
            Chair or NewChair => PieceFamily.Chair,
            KnownObject.Monitor or MonitorDead or Computer or ComputerDead => PieceFamily.Computer,
            Cabinet => PieceFamily.Cabinet,
            Shelves => PieceFamily.Shelf,
            Crate => PieceFamily.Crate,
            Boxes => PieceFamily.Boxes,
            Barrel or RadioactiveBarrel => PieceFamily.Barrel,
            Cylinder => PieceFamily.Cylinder,
            Container or Container2 or Container2Alt => PieceFamily.Container,
            Truck => PieceFamily.Truck,
            Missile => PieceFamily.Missile,
            TestTubes or LabEquipment or ExperimentFlasks or RadioactiveFlask => PieceFamily.Labware,
            BigComputer => PieceFamily.Console,
            BioContainer => PieceFamily.Bank,
            Transformer or ElectricPanel or PetrolGenerator => PieceFamily.Machine,
            Plant => PieceFamily.Plant,
            WaterCylinder => PieceFamily.Water,
            StreetLamp or Windows or Boards or Books or GymEquipment or Toilet or Telephone or RecordPlayer or TrashCan or Television => PieceFamily.Household,
            Generator => PieceFamily.Generator,
            DynamiteSite => PieceFamily.Site,
            _ => null
        };

        /// <summary>Gets a value indicating whether the ID is a blood pool.</summary>
        public bool IsBlood => id is SmallBloodPool or BloodPool or VerticalBloodPool or LargeBloodPool;

        /// <summary>Gets a value indicating whether the ID is a corpse.</summary>
        public bool IsCorpse => id is ScientistCorpse or SoldierCorpse;

        /// <summary>Gets a value indicating whether the ID is a chair.</summary>
        public bool IsChair => id is Chair or NewChair;

        /// <summary>Gets a value indicating whether the ID is a basement door or door frame.</summary>
        public bool IsDoor => id is SmallDoor or DoubleDoor or SmallDoorFrame or DoubleDoorFrame;

        /// <summary>Gets a value indicating whether the ID lies under another object it belongs to, a shadow or the collision dummy of a walk-through sprite.</summary>
        public bool IsAttached => id is EmbeddedShadow or SmallShadow or ZDummy;

        /// <summary>Gets a value indicating whether the ID hangs on a wall above the floor.</summary>
        public bool IsWallMounted => id is KnownObject.Monitor or MonitorDead;

        /// <summary>Gets a value indicating whether the ID is set dressing.</summary>
        public bool IsDecor =>
            !(FloorsSet.Contains(id) || BasementWallsSet.Contains(id) || ConcreteWallsSet.Contains(id) || id is LatticeWall or LatticeFence or TornLattice || GameplaySet.Contains(id)
              || MonstersSet.Contains(id) || BossesSet.Contains(id) || id.IsPickup || id is >= Slug and < (KnownObject)100);
    }
}
