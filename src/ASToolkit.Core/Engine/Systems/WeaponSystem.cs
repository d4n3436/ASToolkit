using System.Numerics;
using ASToolkit.Core.Formats;
using System.Drawing;

namespace ASToolkit.Core.Engine;

internal sealed class WeaponSystem
{
    private const int AimAttempts = 5;
    private const float ScatterScale = 6.1037019E-05f;
    public const float MinBlastHeight = 20;
    private const float AimRise = 70;
    private const float AimRiseCap = 50;
    private const float UnitAimLift = 80;
    private const float GroundAimLift = 19;
    private const int LegSlack = 63;
    private static readonly Vector2 ViewHalf = new(640, 360);
    private readonly Map _map;
    private readonly SpriteCatalog _definitions;
    private readonly Random _rng;
    private readonly SpriteCollector _collector;
    private const int WeaponIdLimit = 21;
    private bool _turnsLegs;
    private readonly DepthProbe _depth;

    public WeaponSystem(Map map, SpriteCatalog definitions, Random random)
    {
        _map = map;
        _definitions = definitions;
        _rng = random;
        _collector = map.Collector;
        Collision = new ProjectileCollision(map.Movement, _collector);
        _depth = new DepthProbe(map);
    }

    public Sprite? Torso { get; set; }

    public ProjectileCollision Collision { get; }

    // Raises the cursor to the height the depth buffer holds under it on screen, or to the terrain off screen
    public Vector3 GetCursorPoint(Sprite man, Vector2 cursor, SpriteDefinition weapon)
    {
        var player = man.Position;
        var pixel = cursor - (player.ToDrawPosition() - ViewHalf);
        float y = cursor.Y;
        float height;

        if (pixel.X >= 0 && pixel.X < ViewHalf.X * 2 && pixel.Y >= 0 && pixel.Y < ViewHalf.Y * 2)
        {
            height = _depth.MeasureHeight(new Point((int)cursor.X, (int)cursor.Y));

            if (height > player.Z + AimRise)
            {
                height = player.Z + AimRiseCap;
            }
        }
        else if (weapon.Targets == SpriteType.Unit)
        {
            height = _map.Movement.GetHeight(cursor with { Y = cursor.Y + UnitAimLift }) + UnitAimLift;
        }
        else
        {
            height = _map.Movement.GetHeight(cursor) + GroundAimLift;
            y -= GroundAimLift;
        }

        return new Vector3(cursor.X, y + height, height);
    }

    [EngineRoutine(0x00455070)]
    public bool ChangeWeapon(Sprite man, int slot)
    {
        var held = man.Definition.LinkDefinition;

        if (held is null || (int)held.Id >= WeaponIdLimit)
            return false;

        if (slot == Sprite.WeaponSlots)
        {
            slot = 0;
        }

        if (!man.Items.Contains(Sprite.WeaponItemBase + slot) || man.Link is null || man.Link.Definition != held)
            return false;

        man.SlotAmmo[man.WeaponSlot] = man.GetAmmo();
        _map.Action(man.Link, ActionCode.ChangeVid, slot + Sprite.WeaponIdBase, (int)SpriteAnimation.Stand);
        man.Definition.LinkDefinition = _definitions.Find((KnownObject)(slot + Sprite.WeaponIdBase));
        man.AddAmmo(man.SlotAmmo[slot] - man.GetAmmo());

        return true;
    }

    // Standing legs follow the torso once its turn passes the slack
    public void TrackCursor(Sprite man, Angle direction, int elapsed)
    {
        var torso = man.Link!;

        if (man is { Speed: 0, Definition.Id: KnownObject.PlayerStart })
        {
            if (!_turnsLegs)
            {
                _turnsLegs = man.Direction.MeasureTurnTo(torso.Direction) > LegSlack;
            }
            else
            {
                _turnsLegs = man.RotateTact(torso.Direction, elapsed) != default;
            }
        }

        torso.RotateTact(direction, elapsed);
    }

    public void FireFrom(Sprite sprite)
    {
        var goal = sprite.Goal;

        if (goal is not null)
        {
            Fire(sprite == Torso ? sprite.Owner! : sprite, sprite, sprite.Definition, goal.Position, goal, 0, sprite.Position, sprite.Direction);
        }
    }


    private void Fire(Sprite shooter, Sprite emitter, SpriteDefinition creator, Vector3 aim, Sprite target, int depth, Vector3? origin = null, Angle? direction = null)
    {
        var child = _definitions.Find((KnownObject)Math.Abs(creator.Children[(int)SpriteAnimation.Fight]));

        if (child is null || depth >= SpriteCatalog.MaxLaunchDepth)
        {
            return;
        }

        bool scatters = !creator.WeaponFlags.HasFlag(WeaponFlags.NoSpread) || target.Definition == _definitions.Empty;
        float spread = creator.Aim;
        var from = origin ?? shooter.Position;
        bool secondPulse = shooter.TakeChildPulse(SpriteAnimation.Fight, creator);
        bool relays = child.Damage <= 0 && child.FightChild != 0;
        foreach (var spawn in ChildEmission.GetChildren(creator, child, SpriteAnimation.Fight, from, direction ?? shooter.Direction, _rng, secondPulse))
        {
            var sprite = relays ? _map.Effects.Spawn(child.Id, spawn.Position, spawn.Direction, _map.Time) : _map.Effects.Launch(child, spawn.Position, spawn.Direction, shooter, _map.Time);

            if (sprite is null)
                continue;

            if (child.Object.SpriteClass == SpriteClass.Linker)
            {
                SpawnedObjectSystem.ConstructLinker(sprite, emitter, spawn.Position, spawn.Direction);
            }

            var goal = target;

            if (scatters)
            {
                var point = aim;
                for (int attempt = 0; attempt < AimAttempts; attempt++)
                {
                    point = Scatter(aim, spawn.Lateral, spread);

                    if (_map.Movement.GetHeight(point.AsVector2()) < aim.Z && _map.Movement.GetHeight((aim.AsVector2() + point.AsVector2()) * 0.5f) < aim.Z)
                        break;
                }

                goal = _map.ConstructGoal(point);
            }

            if (relays)
            {
                Fire(shooter, sprite, child, goal.Position, goal, depth + 1, spawn.Position, spawn.Direction);
                continue;
            }

            _map.Effects.Aim(sprite, goal);
        }
    }

    public void ApplyBlast(Sprite blast)
    {
        var payload = blast.Definition;

        if (payload.Damage == 0)
            return;

        var impact = blast.Position;
        var point = impact.AsVector2();
        var reach = payload.Half + new Vector2(payload.BlastRadius);
        float heightReach = Math.Max(MinBlastHeight, payload.Object.Size.Z);
        bool scales = payload.IsExplosive && !payload.BlastRadius.IsZero();
        int damage = payload.Damage;
        
        for (var sprite = _collector.First(point - reach, point + reach); sprite is not null; sprite = _collector.NextHashInBox())
        {
            if (sprite == blast || sprite.Definition.MaxHp == 0 || payload.SkipsOwnArmy && sprite.Army == blast.Army)
                continue;

            var victim = sprite.Position;
            var offset = Vector2.Abs(victim.AsVector2() - point);
            float top = victim.Z + sprite.Definition.Object.Size.Z;

            if (offset.X >= reach.X + sprite.Definition.Half.X || offset.Y >= reach.Y + sprite.Definition.Half.Y ||
                MathF.Abs(impact.Z - victim.Z) >= heightReach + sprite.Definition.Object.Size.Z ||
                top < _map.Movement.GetHeight((victim.AsVector2() + point) * 0.5f) ||
                top < _map.Movement.GetHeight((point * 3 + victim.AsVector2()) * 0.25f) ||
                top < _map.Movement.GetHeight((victim.AsVector2() * 3 + point) * 0.25f))
                continue;

            float distance = victim.AsVector2().MeasureOctagonalDistance(point);

            if (scales && distance > payload.BlastRadius)
                continue;

            _map.Action(sprite, ActionCode.Damage, scales ? (int)(damage - damage * distance / payload.BlastRadius) : damage);
        }
    }

    private Vector3 Scatter(Vector3 goal, Vector2 lateral, float spread)
    {
        var point = new Vector2(spread) + goal.AsVector2() + lateral - new Vector2(_rng.Next(), _rng.Next()) * spread * ScatterScale;

        return new Vector3(point, goal.Z);
    }


}
