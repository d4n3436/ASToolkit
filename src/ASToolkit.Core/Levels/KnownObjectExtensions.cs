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

    private static readonly FrozenSet<KnownObject> BossesSet = new[] { Boss1, Boss2, Boss3, Boss4, TankBoss, RedTankWeaponBoss, PowerfulBoss3, Bat, Diver, DiverBoss, FinalMonster }.ToFrozenSet();

    private static readonly FrozenSet<KnownObject> ObjectiveMonstersSet = MonstersSet.Append(Bat).Append(Diver).ToFrozenSet();

    private static readonly FrozenSet<KnownObject> FloorsSet = new[] { BasementFloor, Earth, BasementFloorAlt, MilitaryFloor, Grass, VolumetricGrass, FlatGrass }.ToFrozenSet();

    private static readonly FrozenSet<KnownObject> LabSlashSet = new[]
    {
        LabWallSlash, LabWall2Slash, SmallDoorWall, SmallDoorWall2, DoubleDoorWall, DoubleDoorWall2,
        BreakableWall, BreakableWall2, SpawnWallSlash, BrokenWall, BrokenWall2
    }.ToFrozenSet();

    private static readonly FrozenSet<KnownObject> LabBackSet = new[] { LabWallBack, SpawnWallBack, LabWall2Back, SmallMonsterHole, BigMonsterHole }.ToFrozenSet();

    private static readonly FrozenSet<KnownObject> LabDoubleSet = new[] { DoubleDoor, DoubleDoorWall, DoubleDoorFrame, DoubleDoorWall2 }.ToFrozenSet();

    private static readonly FrozenSet<KnownObject> LabWallsSet = new[]
    {
        LabWallSlash, LabWallBack, LabBeam, SmallDoor, SmallDoorWall, DoubleDoor, DoubleDoorWall, BreakableWall, BrokenWall, DoubleDoorFrame, SmallMonsterHole, RubblePile,
        BigMonsterHole, SmallDoorFrame, SpawnWallSlash, SpawnWallBack, LabWall2Slash, LabWall2Back, LabBeam2, SmallDoorWall2, DoubleDoorWall2, BreakableWall2, BrokenWall2
    }.ToFrozenSet();

    private static readonly FrozenSet<KnownObject> MilitaryWallsSet = new[] { MilitaryWallBack, MilitaryWallSlash, MilitaryCorner, FenceEmitter, MilitaryDoorWall }.ToFrozenSet();

    private static readonly FrozenSet<KnownObject> GameplaySet = new[] 
    {
        Cursor, UndefinedObject7, UndefinedObject8, PlayerStart, PlayerStartGirl, DynamiteSite, ObjectMarker, Fence, Generator, GeneratorDead, Breaker,
        KeypadOn, KeypadOff, MountedCannon, TeleportPad, SilentTeleportPad, GeneratorSound, Spawner, ShakeDummy, SpawnDummy, ShootHere, ShootHereSpawner,
        PutDynamiteHere, PutDynamiteHereSpawner, BigExplosion, ExplosionSpark, FlyingGibs
    }.Concat(Enumerable.Range((int)FirstEndlessGenerator, LastEndlessGenerator - FirstEndlessGenerator + 1).Select(id => (KnownObject)id)).ToFrozenSet();

    private static readonly KnownObject[] BloodPoolArray = [BloodPool, LargeBloodPool];

    extension(KnownObject)
    {
        /// <summary>Gets the IDs of the dried blood pools the originals lay under fresh corpses.</summary>
        public static IReadOnlyList<KnownObject> BloodPools => BloodPoolArray;

        /// <summary>Gets the IDs of the ordinary monsters.</summary>
        public static FrozenSet<KnownObject> Monsters => MonstersSet;

        /// <summary>Gets the IDs of the monsters that can be a level's first objective: the ordinary monsters plus the bat and the diver.</summary>
        public static FrozenSet<KnownObject> ObjectiveMonsters => ObjectiveMonstersSet;

        /// <summary>Gets the IDs of the bosses and special monsters.</summary>
        public static FrozenSet<KnownObject> Bosses => BossesSet;

        /// <summary>Gets the IDs of the floor tiles.</summary>
        public static FrozenSet<KnownObject> Floors => FloorsSet;

        /// <summary>Gets the IDs of the lab wall pieces that run along '/'.</summary>
        public static FrozenSet<KnownObject> LabSlash => LabSlashSet;

        /// <summary>Gets the IDs of the lab wall pieces that run along '\'.</summary>
        public static FrozenSet<KnownObject> LabBack => LabBackSet;

        /// <summary>Gets the IDs of the double doors and frames, two wall pieces wide.</summary>
        public static FrozenSet<KnownObject> LabDouble => LabDoubleSet;

        /// <summary>Gets the IDs of every lab wall, door and post piece.</summary>
        public static FrozenSet<KnownObject> LabWalls => LabWallsSet;

        /// <summary>Gets the IDs of every military wall piece.</summary>
        public static FrozenSet<KnownObject> MilitaryWalls => MilitaryWallsSet;

        /// <summary>
        /// Returns the ID of the pickup inside a crate payload, or 0 for an empty crate.
        /// </summary>
        public static KnownObject GetCrateItem(ReadOnlySpan<byte> payload) =>
            payload.Length >= 16 && BinaryPrimitives.ReadUInt32LittleEndian(payload[4..]) == 1
                ? (KnownObject)BinaryPrimitives.ReadUInt32LittleEndian(payload[8..])
                : 0;
    }

    extension(KnownObject id)
    {
        /// <summary>Gets a value indicating whether the ID is a pickup.</summary>
        public bool IsPickup => id is >= (KnownObject)200 and <= MagmaMinigun or >= Dynamite and <= MagmaMinigunAmmo;

        /// <summary>Gets a value indicating whether the ID is an endless monster generator.</summary>
        public bool IsEndlessGenerator => id is >= FirstEndlessGenerator and <= LastEndlessGenerator;

        /// <summary>Gets a value indicating whether the ID is set dressing.</summary>
        public bool IsDecor =>
            !(FloorsSet.Contains(id) || LabWallsSet.Contains(id) || MilitaryWallsSet.Contains(id) || id is LatticeWall or LatticeFence or TornLattice || GameplaySet.Contains(id)
              || MonstersSet.Contains(id) || BossesSet.Contains(id) || id.IsPickup || id is >= Slug and < (KnownObject)100);
    }
}
