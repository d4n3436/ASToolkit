using System.Numerics;
using ASToolkit.Core.Formats;
using System.Drawing;

namespace ASToolkit.Core.Engine;

internal sealed class WeaponSystem
{
    private const int AimAttempts = 5;
    public const float MinBlastHeight = 20;
    private const float AimRise = 70;
    private const float AimRiseCap = 50;
    private const float UnitAimLift = 80;
    private const float GroundAimLift = 19;
    private const int LegSlack = 63;
    private static readonly Vector2 ViewHalf = new(640, 360);
    private readonly GameWorld _world;
    private readonly SpriteCatalog _definitions;
    private readonly Random _rng;
    private readonly UnitGrid _grid;
    private const int WeaponIdLimit = 21;
    private bool _turnsLegs;
    private readonly DepthProbe _depth;

    public WeaponSystem(GameWorld world, SpriteCatalog definitions, Random random)
    {
        _world = world;
        _definitions = definitions;
        _rng = random;
        _grid = world.Units;
        Collision = new ProjectileCollision(world.Movement, _grid);
        Torso = CreateTorso(world.Player.Definition.LinkDefinition!, 0, false);
        _depth = new DepthProbe(world);
    }

    public void AttachTorso() => Torso = CreateTorso(_world.Player.Definition.LinkDefinition!, 0, false);

    [EngineRoutine(0x00445770)]
    public void StartTorso(SpriteAnimation state)
    {
        Torso.Start(state, _world.Player.Direction, _world.Time, _rng);
        Torso.CreatedAt = _world.Time;
        _world.Effects.CreateLink(Torso, _world.Time, 0);
    }

    public Unit Torso { get; private set; }

    public ProjectileCollision Collision { get; }

    public bool CanFire => !Torso.IsReloading;

    private Unit CreateTorso(SpriteDefinition definition, int timer, bool linked = true)
    {
        var player = _world.Player;
        var torso = new Unit(_world.AllocateNumber(), definition, GetTorsoPosition(definition), 0, 0) { Direction = player.Direction, Timer = timer, Owner = player };
        torso.ChangeAnimation(SpriteAnimation.Stand);
        player.Link = torso;

        if (linked)
        {
            _world.Effects.CreateLink(torso, _world.Time, 0);
        }

        return torso;
    }

    private Vector3 GetTorsoPosition(SpriteDefinition definition) =>
        _world.Player.Position + _world.Player.Definition.GetLinkOffset(definition, _world.Player.Direction);

    public void UpdateTorso()
    {
        Torso.Position = GetTorsoPosition(Torso.Definition);
        _world.Tact(Torso);
    }

    public int Slot => _world.Player.WeaponSlot;

    [EngineRoutine(0x00454800)]
    public void AimPlayer(Vector2 cursor)
    {
        var weapon = _world.Player.Definition.LinkDefinition;

        if (Torso.Definition != weapon || weapon.FightChild == 0 || weapon.Object.WeaponIndex == 0 || Torso.IsReloading)
            return;

        var point = GetCursorPoint(cursor, weapon);
        _world.DrawGoalSpriteRandom();
        UnitKinematics.SetCommand(Torso, UnitCommand.OneAttack, point: point);
    }

    // Raises the cursor to the height the depth buffer holds under it on screen, or to the terrain off screen
    private Vector3 GetCursorPoint(Vector2 cursor, SpriteDefinition weapon)
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

    [EngineRoutine(0x00455070)]
    public bool SelectWeapon(int slot)
    {
        var player = _world.Player;
        var held = player.Definition.LinkDefinition;

        if (held is null || (int)held.Id >= WeaponIdLimit)
            return false;

        if (slot == Unit.WeaponSlots)
        {
            slot = 0;
        }

        if (!player.Items.Contains(Unit.WeaponItemBase + slot) || player.Link is null || player.Link.Definition != held)
            return false;

        player.SlotAmmo[player.WeaponSlot] = player.GetAmmo();
        var weapon = _definitions.Find((KnownObject)(slot + Unit.WeaponIdBase));

        if (weapon is not null)
        {
            ChangeTorso(weapon);
        }

        player.Definition.LinkDefinition = weapon;
        player.AddAmmo(player.SlotAmmo[slot] - player.GetAmmo());

        return true;
    }

    [EngineRoutine(0x00445770)]
    public void ReplaceTorso()
    {
        var link = _world.Player.Definition.LinkDefinition;

        if (link is null)
            return;

        _world.DropLinks(Torso);
        var carried = Torso.Link;
        Torso.Link = null;
        Torso = CreateTorso(link, 0);
        _world.Register(UpdateRole.Torso, Torso);

        if (carried is not null)
        {
            carried.Owner = null;
            SpawnedObjectSystem.AppendLink(Torso, carried);
        }
    }

    [EngineRoutine(0x00440830)]
    private void ChangeTorso(SpriteDefinition weapon)
    {
        int layer = Torso.Definition.Layer;
        _world.DropLinks(Torso);
        Torso.ChangeVid(weapon, SpriteAnimation.Stand);
        _world.Effects.CreateLink(Torso, _world.Time, 0);

        if (weapon.Layer != layer)
        {
            _world.Register(UpdateRole.Torso, Torso);
        }
    }

    // Standing legs follow the torso once its turn passes the slack
    [EngineRoutine(0x0043ea10)]
    public void TrackCursor(Angle direction, int elapsed)
    {
        var player = _world.Player;

        if (player is { Speed: 0, Definition.Id: KnownObject.PlayerStart })
        {
            if (!_turnsLegs)
            {
                _turnsLegs = player.Direction.MeasureTurnTo(Torso.Direction) > LegSlack;
            }
            else
            {
                player.SetDirection(player.Direction.TurnToward(Torso.Direction, player.Definition.Object.RotationPeriod, elapsed, out int remaining));
                _turnsLegs = remaining != 0;
            }
        }

        Torso.SetDirection(Torso.Direction.TurnToward(direction, Torso.Definition.Object.RotationPeriod, elapsed, out _));
    }

    [EngineRoutine(0x00454800)]
    public void DecidePlayer(int elapsed)
    {

        if (_world.Player.HasGoal || Torso.HasGoal)
        {
            Torso.TryAttack(Math.Max(elapsed, _world.Player.Definition.Layout.GetFrameDuration(_world.Player.Animation)));
        }
    }

    public void FireFrom(Unit unit)
    {
        if (unit.HasGoal)
        {
            Fire(unit == Torso ? _world.Player : unit, unit.Definition, unit.GoalPosition, unit.Goal, 0, unit.Position, unit.Direction);
        }
    }


    private void Fire(Unit shooter, SpriteDefinition creator, Vector3 aim, Unit? target, int depth, Vector3? origin = null, Angle? direction = null)
    {
        var child = _definitions.Find((KnownObject)Math.Abs(creator.Children[(int)SpriteAnimation.Fight]));

        if (child is null || depth >= SpriteCatalog.MaxLaunchDepth)
        {
            return;
        }

        bool scatters = !creator.WeaponFlags.HasFlag(WeaponFlags.NoSpread) || target is null;
        float spread = creator.Aim;
        var from = origin ?? shooter.Position;
        bool secondPulse = shooter.TakeChildPulse(SpriteAnimation.Fight, creator);
        bool relays = child.Damage <= 0 && child.FightChild != 0;
        foreach (var spawn in ChildEmission.GetChildren(creator, child, SpriteAnimation.Fight, from, direction ?? shooter.Direction, _rng, secondPulse))
        {
            var unit = relays ? _world.Effects.Spawn(child.Id, spawn.Position, spawn.Direction, _world.Time) : _world.Effects.Launch(child, spawn.Position, spawn.Direction, shooter, _world.Time);

            if (unit is null)
                continue;

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

            if (relays)
            {
                Fire(shooter, child, point, target, depth + 1, spawn.Position, spawn.Direction);
                continue;
            }

            _world.Effects.Aim(unit, scatters ? null : target, point);
        }
    }

    [EngineRoutine(0x00440830)]
    public void ApplyBlast(Unit blast)
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
        
        for (var unit = _grid.First(point - reach, point + reach); unit is not null; unit = _grid.Next())
        {
            if (unit == blast || unit.Definition.MaxHp == 0 || payload.SkipsOwnArmy && unit.Army == blast.Army)
                continue;

            var victim = unit.Position;
            var offset = Vector2.Abs(victim.AsVector2() - point);
            float top = victim.Z + unit.Definition.Object.Size.Z;

            if (offset.X >= reach.X + unit.Definition.Half.X || offset.Y >= reach.Y + unit.Definition.Half.Y ||
                MathF.Abs(impact.Z - victim.Z) >= heightReach + unit.Definition.Object.Size.Z ||
                top < _world.Movement.GetHeight((victim.AsVector2() + point) * 0.5f) ||
                top < _world.Movement.GetHeight((point * 3 + victim.AsVector2()) * 0.25f) ||
                top < _world.Movement.GetHeight((victim.AsVector2() * 3 + point) * 0.25f))
                continue;

            float distance = victim.AsVector2().MeasureOctagonalDistance(point);

            if (scales && distance > payload.BlastRadius)
                continue;

            _world.Damage(unit, scales ? (int)(damage - damage * distance / payload.BlastRadius) : damage);
        }
    }

    private Vector3 Scatter(Vector3 point, float aim) =>
        point + new Vector3(aim - _rng.NextSingle() * 2 * aim, aim - _rng.NextSingle() * 2 * aim, 0);


}
