using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Simulation;

internal sealed class CombatKind
{
    private const uint SkipsScatterFlag = 0x20;

    public CombatKind(KnownObject id, in GameObject obj, in WeaponRecord weapon, int spriteSpeed)
    {
        Id = id;
        MaxHp = obj.MaxHp;
        Speed = obj.Speed;
        Half = new Vector2(obj.Size.X * obj.Scale.X * 0.5f, obj.Size.Y * obj.Scale.Y * 0.5f);
        MoveMask = obj.MoveMask;
        Damage = obj.Damage;
        BlastRadius = obj.BlastRadius;
        IsExplosive = obj.Flags.HasFlag(ObjectFlags.Explosive);
        SkipsOwnArmy = obj.Flags.HasFlag(ObjectFlags.Projectile);
        IsHoming = obj.Flags.HasFlag(ObjectFlags.Homing);
        IsFlying = obj.Flags.HasFlag(ObjectFlags.Flying);
        DetectRange = weapon.DetectRange;
        BattleRange = weapon.BattleRange;
        MinRange = weapon.MinRange;
        Aim = (weapon.Flags & SkipsScatterFlag) == 0 ? weapon.Aim : 0;
        AmmoCapacity = weapon.Ammo;
        BuildTime = weapon.BuildTime;
        AttackTime = Math.Max(weapon.ReloadTime, GetAnimationTime(obj, SpriteAnimation.Fight, spriteSpeed));
        StandTime = GetAnimationTime(obj, SpriteAnimation.Stand, spriteSpeed);
        FightChild = (KnownObject)obj.Children[(int)SpriteAnimation.Fight];
        FightCount = Math.Max(1, obj.ChildCounts[(int)SpriteAnimation.Fight]);
        StandChild = (KnownObject)obj.Children[(int)SpriteAnimation.Stand];
        DeathChild = (KnownObject)obj.Children[(int)SpriteAnimation.Death];
    }

    public KnownObject Id { get; }

    public int MaxHp { get; }

    public float Speed { get; }

    public Vector2 Half { get; }

    public uint MoveMask { get; }

    public int Damage { get; }

    public float BlastRadius { get; }

    public bool IsExplosive { get; }

    public bool SkipsOwnArmy { get; }

    public bool IsHoming { get; }

    public bool IsFlying { get; }

    public float DetectRange { get; }

    public float BattleRange { get; }

    public float MinRange { get; }

    public float Aim { get; }

    public int AmmoCapacity { get; }

    public int BuildTime { get; }

    public int AttackTime { get; }

    public int StandTime { get; }

    public KnownObject FightChild { get; }

    public int FightCount { get; }

    public KnownObject StandChild { get; }

    public KnownObject DeathChild { get; }

    public KnownObject[] DeathSpawns { get; init; } = [];

    public KnownObject[] Death2Spawns { get; init; } = [];

    public bool IsArmed => BattleRange > 0;

    private static int GetAnimationTime(in GameObject obj, SpriteAnimation animation, int spriteSpeed) =>
        obj.FrameCounts[(int)animation] * obj.GetFrameDuration(animation, spriteSpeed);
}
