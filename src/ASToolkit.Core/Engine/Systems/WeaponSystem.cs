using System.Numerics;
using ASToolkit.Core.Formats;
using System.Drawing;

namespace ASToolkit.Core.Engine;

internal sealed class WeaponSystem
{
    private const int AimAttempts = 5;
    private const float MinBlastHeight = 20;
    private const float AimRise = 70;
    private const float AimRiseCap = 50;
    private const float UnitAimLift = 80;
    private const float GroundAimLift = 19;
    private const int LegSlack = 63;
    private static readonly Vector2 ViewHalf = new(640, 360);
    private readonly GameWorld _world;
    private readonly PlayerInventory _inventory;
    private readonly ObjectCatalog _kinds;
    private readonly Random _rng;
    private readonly UnitGrid _grid;
    private readonly List<Unit> _nearby = [];
    private int _slot;
    private bool _turnsLegs;
    private readonly DepthProbe _depth;

    public WeaponSystem(GameWorld world, PlayerInventory inventory, ObjectCatalog kinds, Random random)
    {
        _world = world;
        _inventory = inventory;
        _kinds = kinds;
        _rng = random;
        _grid = world.Units;
        Collision = new ProjectileCollision(world.Movement, _grid);
        Torso = CreateTorso(kinds.Find(KnownObject.PistolsTorso)!, 0);
        _depth = new DepthProbe(world);
    }

    public Unit Torso { get; private set; }

    public ProjectileCollision Collision { get; }

    public bool CanFire => !Torso.IsReloading;

    public UnitState GetPlayerState() => _world.Player.GetState() with { Weapon = Torso.Kind.Id };

    private Unit CreateTorso(ObjectDefinition kind, int timer)
    {
        var torso = new Unit(_world.AllocateNumber(), kind, GetTorsoPosition(kind), 0, 0) { Facing = _world.Player.Facing, Timer = timer };
        torso.SetState(SpriteAnimation.Stand);

        return torso;
    }

    private Vector3 GetTorsoPosition(ObjectDefinition kind) =>
        _world.Player.Position + _world.Player.Kind.Object.GetLinkOffset(kind.Object, null, 0, _world.Player.Facing.Value);

    public void UpdateTorso()
    {
        Torso.Position = GetTorsoPosition(Torso.Kind);
        _world.TickUnit(Torso);
    }

    [EngineRoutine(0x00454800)]
    public void AimPlayer(Vector2 cursor, int slot)
    {
        if (_world.IsInCannon)
        {
            slot = PlayerInventory.Pistols;
        }

        var weapon = _world.IsInCannon ? _kinds.Find(KnownObject.MannedCannonTurret) : _inventory.GetWeapon(slot);

        if (weapon is null || !weapon.IsArmed)
            return;

        if (Torso.Kind != weapon)
        {
            Torso = CreateTorso(weapon, Torso.Timer);
            _world.Register(UpdateRole.Torso, Torso);
        }

        _slot = slot;

        if (!Torso.IsReloading)
        {
            UnitKinematics.SetCommand(Torso, UnitCommand.Engage, point: GetCursorPoint(new Vector2((int)cursor.X, (int)cursor.Y), weapon));
        }
    }

    // Raises the cursor to the height the depth buffer holds under it on screen, or to the terrain off screen
    private Vector3 GetCursorPoint(Vector2 cursor, ObjectDefinition weapon)
    {
        var player = _world.Player.Position;
        var pixel = cursor - (new Vector2(player.X, player.Y - player.Z) - ViewHalf);
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
            height = _world.Movement.GetHeight(cursor with { Y = cursor.Y + UnitAimLift }) + UnitAimLift;
        }
        else
        {
            height = _world.Movement.GetHeight(cursor) + GroundAimLift;
            y -= GroundAimLift;
        }

        return new Vector3(cursor.X, y + height, height);
    }

    // Turns the torso toward the cursor each frame and the standing legs after it once it passes the slack
    [EngineRoutine(0x0043ea10)]
    public void TrackCursor(Vector2 cursor, int elapsed, bool moves)
    {
        var player = _world.Player;
        var facing = Facing.Aim(new Vector2(cursor.X - player.Position.X, player.Position.Z + cursor.Y - player.Position.Y));

        if (!moves && player is { Speed: 0, Kind.Id: KnownObject.PlayerStart })
        {
            if (!_turnsLegs)
            {
                _turnsLegs = player.Facing.MeasureTurnTo(Torso.Facing) > LegSlack;
            }
            else
            {
                player.SetFacing(player.Facing.TurnToward(Torso.Facing, player.Kind.Object.TurnPeriod, elapsed, out int remaining));
                _turnsLegs = remaining != 0;
            }
        }

        Torso.SetFacing(Torso.Facing.TurnToward(facing, Torso.Kind.Object.TurnPeriod, elapsed, out _));
    }

    [EngineRoutine(0x00454800)]
    public void DecidePlayer(int elapsed)
    {

        if (_world.Player.HasGoal || Torso.HasGoal)
        {
            Torso.TryAttack(Math.Max(elapsed, _world.Player.Kind.Layout.GetFrameDuration(_world.Player.Animation)), _inventory, _slot);
        }
    }

    public void ResetPlayer()
    {
        UnitKinematics.SetCommand(Torso, UnitCommand.None);
        Torso.Timer = 0;
        Torso.SetState(SpriteAnimation.Stand);
    }

    public void FireFrom(Unit unit)
    {
        if (unit.HasGoal)
        {
            Fire(unit == Torso ? _world.Player : unit, unit.Kind, unit.GoalPosition, unit.Goal, 0, unit.Position, unit.Facing);
        }
    }


    private void Fire(Unit shooter, ObjectDefinition creator, Vector3 aim, Unit? target, int depth, Vector3? origin = null, Facing? facing = null)
    {
        var child = _kinds.Find((KnownObject)Math.Abs(creator.Object.Children[(int)SpriteAnimation.Fight]));

        if (child is null || depth >= ObjectCatalog.MaxLaunchDepth)
        {
            return;
        }

        bool scatters = !creator.WeaponFlags.HasFlag(WeaponFlags.NoSpread) || target is null;
        float spread = shooter == _world.Player && creator.IsArmed && depth == 0 ? _inventory.Implants.ScaleAim(creator.Aim) : creator.Aim;
        var from = origin ?? shooter.Position;
        bool secondPulse = shooter.TakeChildPulse(SpriteAnimation.Fight, creator);
        foreach (var spawn in ChildEmission.GetChildren(creator, child, SpriteAnimation.Fight, from, facing ?? shooter.Facing, _rng, secondPulse))
        {
            var point = aim;

            if (scatters)
            {
                var center = aim + spawn.Lateral.AsVector3();
                for (int attempt = 0; attempt < AimAttempts; attempt++)
                {
                    point = Scatter(center, spread);

                    if (_world.Movement.GetHeight(point.AsVector2()) < aim.Z && _world.Movement.GetHeight((aim.AsVector2() + point.AsVector2()) * 0.5f) < aim.Z)
                        break;
                }
            }

            if (child.Damage <= 0 && child.FightChild != 0)
            {
                _world.Effects.Spawn(child.Id, spawn.Position, spawn.Facing, _world.Time);
                Fire(shooter, child, point, target, depth + 1, spawn.Position, spawn.Facing);
                continue;
            }

            _world.Effects.Launch(child, spawn.Position, spawn.Facing, shooter, scatters ? null : target, point, _world.Time);
            _world.RecordShot(new ShotTrace(child.Id, spawn.Position, point, _world.Time, _world.Time));
        }
    }

    [EngineRoutine(0x00440830)]
    public void ApplyBlast(ObjectDefinition payload, Vector3 impact, Unit? shooter = null)
    {
        if (payload.Damage == 0)
            return;

        var point = impact.AsVector2();
        var reach = payload.Half + new Vector2(payload.BlastRadius);
        float heightReach = Math.Max(MinBlastHeight, payload.Object.Size.Z);
        bool scales = payload.IsExplosive && !payload.BlastRadius.IsZero();
        _grid.Collect(point - reach - new Vector2(UnitGrid.MaxHalfSize), point + reach + new Vector2(UnitGrid.MaxHalfSize), _nearby);
        foreach (var unit in _nearby)
        {
            if (unit.Kind.MaxHp == 0 || shooter is not null && unit.Army == shooter.Army && payload.SkipsOwnArmy)
                continue;

            var victim = unit.Position;
            var offset = Vector2.Abs(victim.AsVector2() - point);
            float top = victim.Z + unit.Kind.Object.Size.Z;

            if (offset.X >= reach.X + unit.Kind.Half.X || offset.Y >= reach.Y + unit.Kind.Half.Y ||
                MathF.Abs(impact.Z - victim.Z) >= heightReach + unit.Kind.Object.Size.Z ||
                top < _world.Movement.GetHeight((victim.AsVector2() + point) * 0.5f) ||
                top < _world.Movement.GetHeight((point * 3 + victim.AsVector2()) * 0.25f) ||
                top < _world.Movement.GetHeight((victim.AsVector2() * 3 + point) * 0.25f))
                continue;

            float distance = victim.AsVector2().MeasureDistance(point);

            if (scales && distance > payload.BlastRadius)
                continue;

            int damage = shooter == _world.Player ? _inventory.Implants.ScaleDamage(payload.Id, payload.Damage) : payload.Damage;
            _world.Damage(unit, scales ? (int)(damage - damage * distance / payload.BlastRadius) : damage);
        }
    }

    private Vector3 Scatter(Vector3 point, float aim) =>
        point + new Vector3(aim - _rng.NextSingle() * 2 * aim, aim - _rng.NextSingle() * 2 * aim, 0);


}
