using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal sealed class ObjectDefinition
{
    public const int Unlimited = 999999;
    public const float PerMillisecond = 0.001f;
    public const float PerMillisecondSquared = 1e-6f;

    private readonly GameObject _object;
    private readonly AnimationDefinition[] _animations;

    public ObjectDefinition(KnownObject id, in GameObject obj, in WeaponRecord weapon, int spriteSpeed, WeaponBehavior behavior = WeaponBehavior.None, int totalFrames = 0, int layer = 0)
    {
        Id = id;
        _object = obj;
        Layout = new FrameLayout(obj, spriteSpeed, totalFrames);
        _animations = new AnimationDefinition[(int)SpriteAnimation.Death2 + 1];
        for (int index = 0; index < _animations.Length; index++)
        {
            var animation = (SpriteAnimation)index;
            _animations[index] = new AnimationDefinition(
                Math.Max(1, obj.FrameCounts[(int)obj.GetVisualAnimation(animation)]),
                Math.Max(1, obj.GetFrameDuration(animation, spriteSpeed)));
        }

        SpriteSpeed = spriteSpeed;
        Layer = layer;
        Speed = ScaleRate(obj.Speed, PerMillisecond);
        ZSpeed = ScaleRate(obj.ZSpeed, PerMillisecond);
        Acceleration = ScaleRate(obj.Acceleration, PerMillisecondSquared);
        Deceleration = ScaleRate(obj.Deceleration, PerMillisecondSquared);
        WeaponFlags = weapon.Flags;
        Behavior = behavior;
        Targets = weapon.Targets;
        Half = obj.Size.AsVector2() * 0.5f;
        DetectRange = weapon.DetectRange;
        BattleRange = weapon.BattleRange;
        MinRange = weapon.MinRange;
        Aim = weapon.Aim;
        AmmoCapacity = weapon.Ammo;
        BuildTime = weapon.BuildTime;
        ReloadTime = weapon.ReloadTime;
        AttackTime = Math.Max(weapon.ReloadTime, GetAnimationTime(obj, SpriteAnimation.Fight, spriteSpeed));
        FightChild = (KnownObject)obj.Children[(int)SpriteAnimation.Fight];
        FightCount = Math.Max(1, obj.ChildCounts[(int)SpriteAnimation.Fight]);
        DeathChild = (KnownObject)obj.Children[(int)SpriteAnimation.Death];
    }

    public KnownObject Id { get; }

    public ref readonly GameObject Object => ref _object;

    public FrameLayout Layout { get; }

    public int SpriteSpeed { get; }

    public WeaponFlags WeaponFlags { get; }

    public WeaponBehavior Behavior { get; }

    public SpriteType Targets { get; }

    public bool CanBePushed => WeaponFlags.HasFlag(WeaponFlags.Pushable);

    public bool AttacksWhileTurning => WeaponFlags.HasFlag(WeaponFlags.AttacksWhileTurning);

    public bool NeedsForwardTarget => WeaponFlags.HasFlag(WeaponFlags.ForwardTargets);

    public bool AttacksTwice => WeaponFlags.HasFlag(WeaponFlags.DoubleAttack);

    public bool AcquiresTargets => Behavior.HasFlag(WeaponBehavior.AcquiresTargets);

    public bool PursuesTargets => Behavior.HasFlag(WeaponBehavior.PursuesTargets);

    public SpriteAnimation InitialAnimation =>
        Layout.GetFrameCount(SpriteAnimation.Stand) == 0 && Layout.GetFrameCount(SpriteAnimation.Go) == 0 &&
        Layout.GetFrameCount(SpriteAnimation.Death) != 0 ? SpriteAnimation.Death
        : Layout.GetFrameCount(SpriteAnimation.Birth) != 0 ? SpriteAnimation.Birth : SpriteAnimation.Stand;

    public int RollInitialFrame(Random random)
    {
        if (Object.Flags.HasFlag(ObjectFlags.StartsOnFirstFrame))
        {
            return 0;
        }

        random.Next(SpriteSpeed + 1);
        int frames = GetVisualFrameCount(InitialAnimation);

        return InitialAnimation == SpriteAnimation.Stand && frames > 1 ? random.Next(frames) : 0;
    }

    public int GetVisualFrameCount(SpriteAnimation animation) => _animations[(int)animation].FrameCount;

    public int GetVisualFrameDuration(SpriteAnimation animation) => _animations[(int)animation].FrameDuration;

    public int GetAnimationDuration(SpriteAnimation animation) => Math.Max(1, Object.FrameCounts[(int)animation]) * GetVisualFrameDuration(animation);

    public int MaxHp => Object.MaxHp;

    public float Speed { get; }

    public int Layer { get; }

    public float ZSpeed { get; }

    public float Acceleration { get; }

    public float Deceleration { get; }

    public Vector2 Half { get; }

    public CollisionVolume GetCollisionVolume(Vector3 position) => new(position, Half, Object.Size.Z, MoveMask);

    public CollisionLayers MoveMask => Object.MoveMask;

    public int Damage => Object.Damage;

    public float BlastRadius => Object.BlastRadius;

    public bool IsExplosive => Object.Flags.HasFlag(ObjectFlags.Explosive);

    public bool SkipsOwnArmy => Object.Flags.HasFlag(ObjectFlags.Projectile);
    
    public float DetectRange { get; }

    public float BattleRange { get; }

    public float MinRange { get; }

    public float Aim { get; }

    public int AmmoCapacity { get; }

    public int BuildTime { get; }

    public int ReloadTime { get; }

    public int AttackTime { get; }
    
    public KnownObject FightChild { get; }

    public int FightCount { get; }
    
    public KnownObject DeathChild { get; }

    public bool IsArmed => BattleRange > 0;

    private static int GetAnimationTime(in GameObject obj, SpriteAnimation animation, int spriteSpeed) =>
        obj.FrameCounts[(int)animation] * obj.GetFrameDuration(animation, spriteSpeed);
    private readonly record struct AnimationDefinition(int FrameCount, int FrameDuration);

    private static float ScaleRate(float rate, float scale) => rate.IsNear(Unlimited) ? rate : rate * scale;
}
