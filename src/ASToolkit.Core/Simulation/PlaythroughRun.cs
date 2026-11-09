using System.Drawing;
using System.Numerics;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Content;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Simulation;

internal sealed class PlaythroughRun
{
    private const int TickTime = 20;
    private const int FrameTime = 100;
    private const int WakeInterval = 5;
    private const int CompactInterval = 50;
    private const int MaxTime = 30 * 60 * 1000;
    private const int SampleCount = 20;
    private const float InstantSpeed = 999999;
    private const int MaxLaunchDepth = 3;
    private const int PlayerArmy = 0;
    private const int MonsterArmy = 1;
    private const int Pistols = 1;
    private const int TorsoBase = 10;
    private const int WeaponPickupBase = 260;
    private const int AmmoPickupBase = 300;
    private const int ArmorObjectOffset = 4;
    private const int UnlimitedAmmo = 999999;
    private const float PickupReach = 60;
    private const float AmmoReach = 50;
    private const float FloodReach = 2000;
    private const int FloodInterval = 200;
    private const float MaxHalfSize = 65;
    private const float MapMargin = 512;
    private const int BlockedWait = 100;
    private const float WaypointReach = 40;
    private const float CrowdReach = 200;

    private const float SiteBlastReach = 100;

    // Bot behavior and stand-ins for engine rules not read yet
    private const float BackOffReach = 140;
    private const float StandOffMargin = 100;
    private const int EscapeSteps = 10;
    private const int GeneratorMaxAlive = 8;
    private const int StaggerTime = 100;
    private const float DetourReach = 800;
    private const int DetourSlack = 5000;

    private static readonly int[] ArmorShares = [50, 70, 90];
    private static readonly Vector2 ScreenHalf = new(FightPlanner.Screen.Width / 2, FightPlanner.Screen.Height / 2);

    private readonly RouteAnalysis _analysis;
    private readonly CombatKindTable _kinds;
    private readonly IReadOnlyList<int> _ammoBatches;
    private readonly Random _rng;
    private readonly bool _records;
    private readonly WalkField _field;
    private readonly UnitGrid _grid;
    private readonly CombatUnit _player;
    private readonly CombatKind?[] _torsos = new CombatKind?[TorsoBase];
    private readonly List<CombatUnit> _monsters = [];
    private readonly List<CombatUnit> _nearby = [];
    private readonly List<CombatUnit> _threats = [];
    private readonly List<Builder> _builders = [];
    private readonly List<Pickup> _pickups = [];
    private readonly List<Flight> _flights = [];
    private readonly List<PlaythroughFrame> _frames = [];
    private readonly List<ShotTrace> _shots = [];
    private readonly List<PlaythroughSample> _samples = [];
    private readonly List<KnownObject> _taken = [];
    private readonly List<Vector2> _way = [];
    private readonly Queue<Vector2> _path = [];
    private readonly Dictionary<int, int[]> _routeFields = [];
    private readonly Vector2[] _tour;
    private readonly float[] _tourLength;
    private readonly int[] _ammo = new int[TorsoBase];
    private readonly bool[] _held = new bool[TorsoBase];
    private readonly bool[] _objectiveDone;
    private int _maxHealth;
    private float _speed;
    private PlayerImplants _implants;
    private int _pathEnds;
    private bool _offTour;
    private bool _doorsLocked;
    private bool _fencesUp;
    private int _time;
    private int _tick;
    private int _cursor;
    private int _farthest;
    private int _nextAttack;
    private int _nextFlood;
    private int _floodCell = -1;
    private int _armorHp;
    private int _armorShare;
    private int _money;
    private int _kills;
    private int _minHealth;
    private int _unitCount;

    public PlaythroughRun(RouteAnalysis analysis, CombatKindTable kinds, IReadOnlyList<int> ammoBatches, PlayerLoadout loadout, Random rng, bool records)
    {
        _analysis = analysis;
        _kinds = kinds;
        _ammoBatches = ammoBatches;
        _rng = rng;
        _records = records;
        var lattice = analysis.Grid.Lattice;
        _tour = analysis.Tour.Select(cell => ToVector(lattice.GetCenter(cell))).ToArray();
        _tourLength = new float[_tour.Length];
        for (int index = 1; index < _tour.Length; index++)
        {
            _tourLength[index] = _tourLength[index - 1] + Vector2.Distance(_tour[index], _tour[index - 1]);
        }

        _objectiveDone = new bool[analysis.Objectives.Count];
        _doorsLocked = analysis.Objectives.Any(objective => objective.Kind == ObjectiveKind.Breaker) && analysis.Grid.Edges.Values.Any(kind => kind == EdgeKind.Door);
        _fencesUp = analysis.Grid.FenceEdges.Count > 0;
        _field = new WalkField(analysis.Grid, _doorsLocked, _fencesUp);
        var centers = analysis.Grid.Cells.Select(cell => ToVector(lattice.GetCenter(cell))).ToArray();
        _grid = new UnitGrid(
            new Vector2(centers.Min(center => center.X), centers.Min(center => center.Y)) - new Vector2(MapMargin),
            new Vector2(centers.Max(center => center.X), centers.Max(center => center.Y)) + new Vector2(MapMargin));
        
        for (int weapon = 0; weapon < TorsoBase; weapon++)
        {
            _torsos[weapon] = kinds.Find((KnownObject)(TorsoBase + weapon));
        }

        var legs = kinds.Find(KnownObject.PlayerStart)!;
        _implants = loadout.Implants;
        _maxHealth = _implants.ScaleHealth(legs.MaxHp);
        _minHealth = _maxHealth;
        _speed = _implants.ScaleSpeed(legs.Speed);
        _player = new CombatUnit(_unitCount++, legs, _tour.Length > 0 ? _tour[0] : Vector2.Zero, PlayerArmy, _maxHealth) { Awake = true };
        _grid.Add(_player);
        _held[Pistols] = true;
        _ammo[Pistols] = UnlimitedAmmo;
        foreach ((var weapon, int ammo) in loadout.Ammo)
        {
            _held[(int)weapon - WeaponPickupBase] = true;
            _ammo[(int)weapon - WeaponPickupBase] = ammo;
        }

        if (loadout.Armor != 0)
        {
            WearArmor(loadout.Armor);
        }

        AddElements();
    }

    public PlaythroughResult Play()
    {
        AddSample();
        while (_time < MaxTime && _player.IsAlive && _cursor < _tour.Length - 1)
        {
            UpdatePlayer();
            UpdateBuilders();
            UpdateMonsters();
            for (int index = _flights.Count - 1; index >= 0; index--)
            {
                var shot = _flights[index];
                if (shot.Arrives > _time)
                    continue;

                _flights.RemoveAt(index);
                Land(shot);
            }

            if (_records && _time % FrameTime == 0)
            {
                AddFrame();
            }

            if (_tick % CompactInterval == 0)
            {
                _monsters.RemoveAll(monster => !monster.IsAlive && _time - monster.DiedAt > FrameTime);
            }

            _time += TickTime;
            _tick++;
        }

        var held = new Dictionary<KnownObject, int>();
        for (int weapon = Pistols + 1; weapon < TorsoBase; weapon++)
        {
            if (_held[weapon])
            {
                held[(KnownObject)(WeaponPickupBase + weapon)] = _ammo[weapon];
            }
        }

        return new PlaythroughResult
        {
            Survived = _player.IsAlive && _tour.Length > 1 && _cursor == _tour.Length - 1,
            Time = _time,
            Progress = GetProgress(_farthest),
            Kills = _kills,
            MinHealth = _minHealth,
            Samples = _samples,
            Frames = _frames,
            Shots = _shots,
            Money = _money,
            Pickups = _taken,
            Implants = _implants,
            Ammo = held,
        };
    }

    private void AddElements()
    {
        foreach (var element in _analysis.Elements)
        {
            var position = new Vector2(element.X, element.Y);
            var kind = _kinds.Find(element.Id);
            var program = element.Program;
            switch (element.Kind)
            {
                case ElementKind.Monster or ElementKind.Boss when kind is not null && kind.MaxHp > 0:
                    AddMonster(kind, position, false);
                    break;
                case ElementKind.Spawner when program is not null:
                    var trigger = program.Trigger;
                    _builders.Add(new Builder(position, trigger is null ? position : ToVector(trigger.Value.Center), trigger?.Radius ?? -1, Math.Max(TickTime, kind?.StandTime ?? 0))
                    {
                        Queue = new Queue<KnownObject>(program.Monsters.Reverse()),
                        Route = program.Waypoints.Reverse().Select(point => new Vector2(point.X, point.Y)).ToArray(),
                    });
                    break;
                case ElementKind.EndlessGenerator when kind is not null:
                    _builders.Add(new Builder(position, position, kind.DetectRange, Math.Max(TickTime, kind.StandTime)) { Built = kind.StandChild });
                    break;
                case ElementKind.Pickup:
                    _pickups.Add(new Pickup(position, element.Id, _field.Find(element.Cell)));
                    break;
                case ElementKind.Crate when element.CrateItem != 0:
                    _pickups.Add(new Pickup(position, element.CrateItem, _field.Find(element.Cell)));
                    break;
            }
        }
    }

    private void UpdatePlayer()
    {
        float progress = GetProgress(_cursor);
        for (int index = 0; index < _objectiveDone.Length; index++)
        {
            if (!_objectiveDone[index] && _analysis.Objectives[index].Progress <= progress)
            {
                _objectiveDone[index] = true;
                Complete(_analysis.Objectives[index]);
            }
        }

        if (_tick % WakeInterval == 0)
        {
            TakePickups();
        }

        _threats.Clear();
        CombatUnit? target = null;
        CombatUnit? threat = null;
        float targetDistance = float.MaxValue;
        float worstIntrusion = float.MinValue;
        foreach (var monster in _grid.GetNear(_player.Position - ScreenHalf, _player.Position + ScreenHalf))
        {
            float distance = Measure(_player.Position, monster.Position);
            float intrusion = GetKeptDistance(monster.Kind) - distance;
            bool isCloser = distance < targetDistance;
            bool threatens = monster.Awake && intrusion > worstIntrusion - StandOffMargin;
            if (monster == _player || !IsOnScreen(monster.Position) || (!isCloser && !threatens) || !_field.CanSee(_player.Position, monster.Position))
                continue;

            if (isCloser)
            {
                target = monster;
                targetDistance = distance;
            }

            if (monster.Awake && intrusion > -StandOffMargin)
            {
                _threats.Add(monster);
            }

            if (monster.Awake && intrusion > worstIntrusion)
            {
                threat = monster;
                worstIntrusion = intrusion;
            }
        }

        if (target is not null && _time >= _nextAttack)
        {
            Shoot(target);
        }

        float step = _speed * TickTime / 1000;
        if (threat is not null && worstIntrusion > 0)
        {
            if (_tick % WakeInterval == 0 || _path.Count == 0 || _pathEnds != int.MaxValue)
            {
                PlanEscape();
            }

            if (_path.Count > 0)
            {
                _offTour = true;
                FollowPath(step);
            }
            else
            {
                Walk(1, step);
            }
        }
        else if (target is not null && (threat is not null ? worstIntrusion > -StandOffMargin : !target.Awake))
        {
            _player.Animation = SpriteAnimation.Fight;
        }
        else if (_offTour)
        {
            _offTour = false;
            int start = _field.Find(_player.Position);
            FloodPlayer(start, (int)(FloodReach / _field.CellStep));
            QueuePath(_field.Find(_tour[_cursor]), _tour[_cursor], false);
        }
        else if (_path.Count > 0 || target is null && threat is null && _tick % WakeInterval == 0 && PlanDetour())
        {
            FollowPath(step);
        }
        else
        {
            Walk(1, step);
        }
    }

    private static float GetKeptDistance(CombatKind kind) => kind.IsArmed ? MathF.Max(BackOffReach, kind.BattleRange + StandOffMargin) : BackOffReach;

    // Heads for the cell within the escape reach that stands farthest outside the ranges of the threats in view
    private void PlanEscape()
    {
        _path.Clear();
        int start = _field.Find(_player.Position);
        FloodPlayer(start, EscapeSteps);
        _floodCell = -1;
        int best = -1;
        float bestScore = float.MinValue;
        for (int order = 0; order < _field.ReachedCount; order++)
        {
            int cell = _field.GetReached(order);
            var center = _field.GetCenter(cell);
            float score = float.MaxValue;
            foreach (var threat in _threats)
            {
                score = MathF.Min(score, Measure(center, threat.Position) - GetKeptDistance(threat.Kind));
            }

            if (score > bestScore)
            {
                best = cell;
                bestScore = score;
            }
        }

        if (best != start)
        {
            QueuePath(best, _field.GetCenter(best), false);
            _pathEnds = int.MaxValue;
        }
    }

    // Goes to the nearest wanted pickup within the detour reach and comes back along the same way
    private bool PlanDetour()
    {
        FloodPlayer(_field.Find(_player.Position), (int)(FloodReach / _field.CellStep));
        int best = -1;
        int bestSteps = (int)(DetourReach / _field.CellStep);
        for (int index = 0; index < _pickups.Count; index++)
        {
            int steps = _pickups[index].Cell >= 0 ? _field.GetDistance(_pickups[index].Cell) : -1;
            if (steps > 0 && steps <= bestSteps && Take(_pickups[index].Id, false))
            {
                best = index;
                bestSteps = steps;
            }
        }

        return best >= 0 && QueuePath(_pickups[best].Cell, _pickups[best].Position, true);
    }

    // Queues the way over the player's flood to a cell and its end point, and back to the player when asked
    private bool QueuePath(int goal, Vector2 end, bool comesBack)
    {
        _path.Clear();
        if (goal < 0 || _field.GetDistance(goal) < 0)
            return false;

        _way.Clear();
        for (int cell = goal; cell >= 0 && _field.GetDistance(cell) > 0; cell = _field.GetDownhill(cell))
        {
            _way.Add(_field.GetCenter(cell));
        }

        var back = _player.Position;
        for (int index = _way.Count - 1; index >= 0; index--)
        {
            _path.Enqueue(_way[index]);
        }

        _path.Enqueue(end);
        for (int index = 0; comesBack && index < _way.Count; index++)
        {
            _path.Enqueue(_way[index]);
        }

        if (comesBack)
        {
            _path.Enqueue(back);
        }

        _pathEnds = _time + (int)(_path.Count * _field.CellStep * 1000 / _speed) + DetourSlack;
        return true;
    }

    private void FollowPath(float step)
    {
        if (_time > _pathEnds)
        {
            _path.Clear();
        }

        while (step > 0 && _path.Count > 0)
        {
            float distance = Vector2.Distance(_player.Position, _path.Peek());
            if (!MoveToward(_player, _path.Peek(), MathF.Min(step, distance)) || distance > step)
                return;

            step -= distance;
            _path.Dequeue();
        }
    }

    private void Walk(int direction, float step)
    {
        while (step > 0)
        {
            int next = Math.Clamp(_cursor + direction, 0, _tour.Length - 1);
            float distance = Vector2.Distance(_player.Position, _tour[next]);
            if (!MoveToward(_player, _tour[next], MathF.Min(step, distance)) || distance > step)
                return;

            step -= distance;
            _cursor = next;
            if (_cursor > _farthest)
            {
                _farthest = _cursor;
                if (GetProgress(_farthest) * SampleCount >= _samples.Count)
                {
                    AddSample();
                }
            }

            if (_cursor == 0 || _cursor == _tour.Length - 1)
                return;
        }
    }

    private void Shoot(CombatUnit target)
    {
        int best = Pistols;
        float bestRate = 0;
        for (int number = 0; number < TorsoBase; number++)
        {
            var torso = _torsos[number];
            if (!_held[number] || _ammo[number] <= 0 || torso is null || !torso.IsArmed)
                continue;

            float rate = _kinds.GetAttackDamage(torso) * 1000f / Math.Max(TickTime, torso.AttackTime);
            if (rate > bestRate)
            {
                best = number;
                bestRate = rate;
            }
        }

        var weapon = _torsos[best]!;
        if (Measure(_player.Position, target.Position) > weapon.BattleRange)
            return;

        Fire(_player, weapon, target.Position, target, weapon.BattleRange, 0);
        _nextAttack = _time + Math.Max(TickTime, weapon.AttackTime);
        _player.Animation = SpriteAnimation.Fight;
        _player.Facing = GetFacing(_player.Position, target.Position);
        if (_ammo[best] < UnlimitedAmmo)
        {
            _ammo[best]--;
        }
    }

    private void UpdateBuilders()
    {
        foreach (var builder in _builders)
        {
            if (builder.Destroyed || _time < builder.NextBuild)
                continue;

            KnownObject next;
            if (builder.Built != 0)
            {
                builder.Alive.RemoveAll(unit => !unit.IsAlive);
                if (builder.Alive.Count >= GeneratorMaxAlive || Measure(_player.Position, builder.Trigger) > builder.Radius)
                    continue;

                next = builder.Built;
            }
            else
            {
                builder.Triggered |= builder.Radius < 0 || Measure(_player.Position, builder.Trigger) <= builder.Radius;
                if (!builder.Triggered || builder.Queue.Count == 0)
                    continue;

                next = builder.Queue.Peek();
            }

            var kind = _kinds.Find(next);
            if (kind is not null && kind.MaxHp > 0 && !IsFree(kind, builder.Position))
                continue;

            if (builder.Built == 0)
            {
                builder.Queue.Dequeue();
            }

            if (kind is null || kind.MaxHp <= 0)
                continue;

            var unit = AddMonster(kind, builder.Position, builder.Route.Length == 0);
            unit.Route = builder.Route;
            if (builder.Built != 0)
            {
                builder.Alive.Add(unit);
            }

            builder.NextBuild = _time + Math.Max(builder.Interval, kind.BuildTime);
        }
    }

    private void UpdateMonsters()
    {
        if (_time >= _nextFlood)
        {
            FloodPlayer(_field.Find(_player.Position), (int)(FloodReach / _field.CellStep));
            _nextFlood = _time + FloodInterval;
        }

        bool checksSight = _tick % WakeInterval == 0;
        int count = _monsters.Count;
        for (int index = 0; index < count; index++)
        {
            var monster = _monsters[index];
            if (!monster.IsAlive)
                continue;

            var kind = monster.Kind;
            float distance = Measure(monster.Position, _player.Position);
            if (!monster.Awake)
            {
                monster.Awake = checksSight && distance <= kind.DetectRange && (distance <= kind.BattleRange || _field.CanSee(monster.Position, _player.Position));
                if (!monster.Awake && monster.RouteStep < monster.Route.Length)
                {
                    FollowRoute(monster);
                }

                continue;
            }

            if (_time < monster.StaggerUntil)
            {
                monster.Animation = SpriteAnimation.Close;
                continue;
            }

            if (checksSight || !monster.SightChecked)
            {
                monster.SeesPlayer = _field.CanSee(monster.Position, _player.Position);
                monster.SightChecked = true;
            }

            if (kind.IsArmed && distance <= kind.BattleRange && distance > kind.MinRange && monster.SeesPlayer)
            {
                monster.Facing = GetFacing(monster.Position, _player.Position);
                monster.Animation = SpriteAnimation.Fight;
                if (_time >= monster.NextAttack)
                {
                    Fire(monster, kind, _player.Position, _player, kind.BattleRange, 0);
                    monster.NextAttack = _time + Math.Max(TickTime, kind.AttackTime);
                }

                continue;
            }

            float step = kind.Speed * TickTime / 1000;
            int cell = _field.Find(monster.Position);
            int next = cell >= 0 && _field.GetDistance(cell) > 0 ? _field.GetDownhill(cell) : -1;
            if (next >= 0)
            {
                MoveToward(monster, _field.GetCenter(next), step);
            }
            else if (cell < 0 || cell == _floodCell || monster.SeesPlayer)
            {
                MoveToward(monster, _player.Position, step);
            }
            else
            {
                monster.Animation = SpriteAnimation.Stand;
            }
        }
    }

    // Walks a spawned unit over the walk field toward its next waypoint, counting a waypoint reached when close or blocked near it
    private void FollowRoute(CombatUnit unit)
    {
        var waypoint = unit.Route[unit.RouteStep];
        float step = unit.Kind.Speed * TickTime / 1000;
        int cell = _field.Find(unit.Position);
        int target = _field.Find(waypoint);
        if (Vector2.DistanceSquared(unit.Position, waypoint) < WaypointReach * WaypointReach)
        {
            unit.RouteStep++;
            return;
        }

        if (cell < 0 || target < 0)
        {
            MoveToward(unit, waypoint, step);
            return;
        }

        if (!_routeFields.TryGetValue(target, out var distance))
        {
            distance = _field.CreateField(target);
            _routeFields[target] = distance;
        }

        int next = distance[cell] > 0 ? _field.GetDownhill(cell, distance) : -1;
        if (next < 0 || (!MoveToward(unit, _field.GetCenter(next), step) && Vector2.DistanceSquared(unit.Position, waypoint) < CrowdReach * CrowdReach))
        {
            unit.RouteStep++;
        }
    }

    private void FloodPlayer(int cell, int maxSteps)
    {
        _field.Flood(cell, maxSteps);
        _floodCell = cell;
    }

    private void Fire(CombatUnit shooter, CombatKind creator, Vector2 aim, CombatUnit target, float range, int depth)
    {
        var child = _kinds.Find(creator.FightChild);
        if (child is null || depth >= MaxLaunchDepth)
            return;

        float spread = shooter == _player && creator.IsArmed && depth == 0 ? _implants.ScaleAim(creator.Aim) : creator.Aim;
        var payload = child.Damage > 0 || child.FightChild != 0 ? child : _kinds.Find(child.DeathChild);
        for (int count = 0; count < creator.FightCount; count++)
        {
            var point = Scatter(aim, spread);
            if (child.Damage <= 0 && child.FightChild != 0)
            {
                Fire(shooter, child, point, target, range, depth + 1);
            }
            else if (payload is not null && payload.Damage > 0)
            {
                Launch(shooter, child, payload, point, target, range);
            }
        }
    }

    private void Launch(CombatUnit shooter, CombatKind carrier, CombatKind payload, Vector2 point, CombatUnit target, float range)
    {
        var offset = point - shooter.Position;
        float distance = offset.Length();
        int flight = carrier.Speed >= InstantSpeed || carrier.Speed <= 0 ? 0 : (int)(distance * 1000 / carrier.Speed);
        bool lands = carrier.IsFlying || carrier.IsHoming || distance <= 0;
        var shot = new Flight(payload, shooter, lands ? point : shooter.Position + offset * (range / distance), carrier.IsHoming ? target : null, lands, _time, _time + flight);
        if (flight == 0)
        {
            Land(shot);
        }
        else
        {
            _flights.Add(shot);
        }
    }

    private void Land(Flight shot)
    {
        var from = shot.Shooter.Position;
        var point = shot.Target is not null && shot.Target.IsAlive ? shot.Target.Position : shot.To;
        var payload = shot.Payload;
        if (!shot.Lands)
        {
            var way = point - from;
            float nearest = _field.TraceSight(from, point);
            var halves = payload.Half + new Vector2(MaxHalfSize);
            foreach (var unit in _grid.GetNear(Vector2.Min(from, point) - halves, Vector2.Max(from, point) + halves))
            {
                if (CanHit(shot, unit))
                {
                    nearest = MathF.Min(nearest, Cross(unit.Position, unit.Kind.Half + payload.Half, from, way));
                }
            }

            point = from + way * nearest;
        }

        if (_records)
        {
            _shots.Add(new ShotTrace(payload.Id, ToPoint(from), ToPoint(point), shot.Fired, _time));
        }

        var reach = payload.Half + new Vector2(payload.BlastRadius + MaxHalfSize);
        _grid.Collect(point - reach, point + reach, _nearby);
        foreach (var unit in _nearby)
        {
            if (CanHit(shot, unit))
            {
                Hit(unit, shot, point);
            }
        }
    }

    private static bool CanHit(Flight shot, CombatUnit unit) => unit.IsAlive && unit != shot.Shooter && (unit.Army != shot.Shooter.Army || !shot.Payload.SkipsOwnArmy);

    // Returns the share of the way at which a shot enters a box, or 1 when it misses
    private static float Cross(Vector2 center, Vector2 reach, Vector2 start, Vector2 way)
    {
        var lower = (center - reach - start) / way;
        var upper = (center + reach - start) / way;
        var near = Vector2.Min(lower, upper);
        var far = Vector2.Max(lower, upper);
        float enter = MathF.Max(0, MathF.Max(float.IsNaN(near.X) ? 0 : near.X, float.IsNaN(near.Y) ? 0 : near.Y));
        float leave = MathF.Min(1, MathF.Min(float.IsNaN(far.X) ? 1 : far.X, float.IsNaN(far.Y) ? 1 : far.Y));
        return enter <= leave ? enter : 1;
    }

    private void Hit(CombatUnit unit, Flight shot, Vector2 point)
    {
        var payload = shot.Payload;
        var reach = unit.Kind.Half + payload.Half + new Vector2(payload.BlastRadius);
        var offset = Vector2.Abs(unit.Position - point);
        float distance = Measure(unit.Position, point);
        bool scales = payload is { IsExplosive: true, BlastRadius: > 0 };
        if (offset.X >= reach.X || offset.Y >= reach.Y || (scales && distance > payload.BlastRadius))
            return;

        int damage = shot.Shooter == _player ? _implants.ScaleDamage(payload.Id, payload.Damage) : payload.Damage;
        Damage(unit, scales ? (int)(damage * (1 - distance / payload.BlastRadius)) : damage);
    }

    private void Damage(CombatUnit unit, int damage)
    {
        if (damage <= 0)
            return;

        if (unit == _player)
        {
            if (_armorHp > 0)
            {
                _armorHp -= (_armorShare * damage + 50) / 100;
                damage -= _armorShare * damage / 100;
            }

            unit.Hp -= damage;
            _minHealth = Math.Min(_minHealth, unit.Hp);
            return;
        }

        unit.Awake = true;
        if (unit.Animation != SpriteAnimation.Fight)
        {
            unit.StaggerUntil = _time + StaggerTime;
        }

        int overkill = damage - unit.Hp;
        unit.Hp -= damage;
        if (unit.IsAlive)
            return;

        _kills++;
        unit.DiedAt = _time;
        _grid.Remove(unit);
        unit.Animation = overkill > unit.Kind.MaxHp ? SpriteAnimation.Death2 : SpriteAnimation.Death;
        foreach (var spawn in unit.Animation == SpriteAnimation.Death2 ? unit.Kind.Death2Spawns : unit.Kind.DeathSpawns)
        {
            AddMonster(_kinds.Find(spawn)!, unit.Position, true);
        }
    }

    private void Complete(RouteObjective objective)
    {
        if (objective.Kind == ObjectiveKind.DynamiteSite)
        {
            foreach (var builder in _builders)
            {
                builder.Destroyed |= builder.Built != 0 && Measure(builder.Position, new Vector2(objective.X, objective.Y)) <= SiteBlastReach;
            }

            return;
        }

        bool opens = (objective.Kind == ObjectiveKind.Breaker && _doorsLocked) || (objective.Kind == ObjectiveKind.Generator && _fencesUp);
        if (!opens)
            return;

        _doorsLocked &= objective.Kind != ObjectiveKind.Breaker;
        _fencesUp &= objective.Kind != ObjectiveKind.Generator;
        _field.SetGates(_doorsLocked, _fencesUp);
        _routeFields.Clear();
        _floodCell = -1;
    }

    private void TakePickups()
    {
        for (int index = _pickups.Count - 1; index >= 0; index--)
        {
            var pickup = _pickups[index];
            float reach = pickup.Id.IsAmmo ? AmmoReach : PickupReach;
            if (Vector2.DistanceSquared(pickup.Position, _player.Position) < reach * reach && Take(pickup.Id, true))
            {
                _taken.Add(pickup.Id);
                _pickups.RemoveAt(index);
            }
        }
    }

    // Tells whether the player wants a pickup and takes it when asked
    private bool Take(KnownObject id, bool apply)
    {
        bool wanted = id switch
        {
            KnownObject.Health or KnownObject.BigHealth => _player.Hp < _maxHealth,
            KnownObject.MegaHealth => _player.Hp < _maxHealth + 100,
            >= KnownObject.GreenArmor and <= KnownObject.RedArmor => _armorHp < (_kinds.Find(id - ArmorObjectOffset)?.MaxHp ?? 0),
            >= KnownObject.LifeImplant and <= KnownObject.AccuracyImplant => IsWithinCap(GetImplanted(id)),
            >= KnownObject.ShotgunAmmo and <= KnownObject.MagmaMinigunAmmo => _ammo[(int)id - AmmoPickupBase] < GetCapacity((int)id - AmmoPickupBase),
            KnownObject.Money or (>= KnownObject.Shotgun and <= KnownObject.MagmaMinigun) => true,
            _ => false,
        };
        if (!wanted || !apply)
            return wanted;

        switch (id)
        {
            case KnownObject.Health or KnownObject.BigHealth or KnownObject.MegaHealth:
                int limit = id == KnownObject.MegaHealth ? _maxHealth + 100 : _maxHealth;
                _player.Hp = Math.Min(limit, _player.Hp + id switch { KnownObject.Health => 20, KnownObject.BigHealth => 50, _ => 100 });
                break;
            case >= KnownObject.GreenArmor and <= KnownObject.RedArmor:
                WearArmor(id);
                break;
            case >= KnownObject.LifeImplant and <= KnownObject.AccuracyImplant:
                _implants = GetImplanted(id);
                _maxHealth = _implants.ScaleHealth(_player.Kind.MaxHp);
                _speed = _implants.ScaleSpeed(_player.Kind.Speed);
                break;
            case KnownObject.Money:
                _money += 50;
                break;
            case >= KnownObject.Shotgun and <= KnownObject.MagmaMinigun:
                _held[(int)id - WeaponPickupBase] = true;
                AddAmmo((int)id - WeaponPickupBase, _ammoBatches[(int)id - WeaponPickupBase] / 2);
                break;
            default:
                AddAmmo((int)id - AmmoPickupBase, _ammoBatches[(int)id - AmmoPickupBase]);
                break;
        }

        return true;
    }

    private PlayerImplants GetImplanted(KnownObject id) => id switch
    {
        KnownObject.LifeImplant => _implants with { Life = _implants.Life + 1 },
        KnownObject.StrengthImplant => _implants with { Strength = _implants.Strength + 1 },
        KnownObject.SpeedImplant => _implants with { Speed = _implants.Speed + 1 },
        _ => _implants with { Accuracy = _implants.Accuracy + 1 },
    };

    private static bool IsWithinCap(PlayerImplants implants) =>
        Math.Max(Math.Max(implants.Life, implants.Strength), Math.Max(implants.Speed, implants.Accuracy)) <= PlayerImplants.MaxCount;

    private int GetCapacity(int weapon) => _torsos[weapon] is null ? 0 : _implants.ScaleAmmo(_torsos[weapon]!.AmmoCapacity);

    private void AddAmmo(int weapon, int amount) => _ammo[weapon] = Math.Min(_torsos[weapon] is null ? amount : GetCapacity(weapon), _ammo[weapon] + amount);

    private void WearArmor(KnownObject pickup)
    {
        _armorHp = _kinds.Find(pickup - ArmorObjectOffset)?.MaxHp ?? 0;
        _armorShare = ArmorShares[pickup - KnownObject.GreenArmor];
    }

    private void AddSample()
    {
        float batches = 0;
        for (int weapon = Pistols + 1; weapon < TorsoBase; weapon++)
        {
            batches += _held[weapon] && _ammoBatches[weapon] > 0 ? (float)_ammo[weapon] / _ammoBatches[weapon] : 0;
        }

        _samples.Add(new PlaythroughSample(GetProgress(_farthest), _time, _player.Hp, Math.Max(0, _armorHp), batches, _money));
    }

    private void AddFrame()
    {
        var units = new List<UnitState> { _player.GetState() };
        foreach (var monster in _monsters)
        {
            if (monster.IsAlive ? monster.Awake || IsOnScreen(monster.Position) : _time - monster.DiedAt < FrameTime)
            {
                units.Add(monster.GetState());
            }
        }

        _frames.Add(new PlaythroughFrame(_time, units));
    }

    private CombatUnit AddMonster(CombatKind kind, Vector2 position, bool awake)
    {
        var unit = new CombatUnit(_unitCount++, kind, position, MonsterArmy, kind.MaxHp) { Awake = awake };
        _monsters.Add(unit);
        _grid.Add(unit);
        return unit;
    }

    // Moves a unit toward a point, sliding it along one axis where another unit stands in the way, and tells whether it made the whole step
    private bool MoveToward(CombatUnit unit, Vector2 goal, float step)
    {
        var start = unit.Position;
        var offset = goal - start;
        float length = offset.Length();
        if (_time < unit.BlockedUntil || length <= 0)
            return length <= 0;

        var moved = start + offset * MathF.Min(1, step / length);
        bool isFull = !Collides(unit, moved);
        var position = isFull ? moved : start with { X = moved.X };
        unit.Facing = GetFacing(start, goal);
        if (!isFull && Collides(unit, position))
        {
            position = start with { Y = moved.Y };
            if (Collides(unit, position))
            {
                unit.Animation = SpriteAnimation.Stand;
                unit.BlockedUntil = unit == _player ? _time : _time + BlockedWait;
                return false;
            }
        }

        unit.Animation = SpriteAnimation.Go;
        unit.Position = position;
        _grid.Move(unit);
        return isFull;
    }

    private bool Collides(CombatUnit unit, Vector2 position)
    {
        var reach = unit.Kind.Half + new Vector2(MaxHalfSize);
        foreach (var other in _grid.GetNear(position - reach, position + reach))
        {
            if (other != unit && Overlaps(unit.Kind, position, other) &&
                Vector2.DistanceSquared(position, other.Position) < Vector2.DistanceSquared(unit.Position, other.Position) && _field.CanSee(unit.Position, other.Position))
                return true;
        }

        return false;
    }

    private bool IsFree(CombatKind kind, Vector2 position)
    {
        var reach = kind.Half + new Vector2(MaxHalfSize);
        foreach (var other in _grid.GetNear(position - reach, position + reach))
        {
            if (Overlaps(kind, position, other))
                return false;
        }

        return true;
    }

    private static bool Overlaps(CombatKind kind, Vector2 position, CombatUnit other)
    {
        var offset = Vector2.Abs(position - other.Position);
        var limit = kind.Half + other.Kind.Half;
        return (other.Kind.MoveMask & kind.MoveMask) != 0 && offset.X < limit.X && offset.Y < limit.Y;
    }

    private bool IsOnScreen(Vector2 point)
    {
        var offset = Vector2.Abs(point - _player.Position);
        return offset.X <= ScreenHalf.X && offset.Y <= ScreenHalf.Y;
    }

    private float GetProgress(int cursor) => _tourLength.Length < 2 ? 1 : _tourLength[cursor] / Math.Max(1, _tourLength[^1]);

    private Vector2 Scatter(Vector2 point, float aim) =>
        aim <= 0 ? point : point + new Vector2(aim - _rng.NextSingle() * 2 * aim, aim - _rng.NextSingle() * 2 * aim);

    private static float Measure(Vector2 from, Vector2 to)
    {
        var offset = Vector2.Abs(to - from);
        return MathF.Max(offset.X, offset.Y) + 0.5f * MathF.Min(offset.X, offset.Y);
    }

    private static byte GetFacing(Vector2 from, Vector2 to) => (byte)(int)MathF.Round(MathF.Atan2(to.Y - from.Y, to.X - from.X) / (2 * MathF.PI) * 256);

    private static Vector2 ToVector(PointF point) => new(point.X, point.Y);

    private static PointF ToPoint(Vector2 vector) => new(vector.X, vector.Y);

    private sealed class Builder
    {
        public Builder(Vector2 position, Vector2 trigger, float radius, int interval)
        {
            Position = position;
            Trigger = trigger;
            Radius = radius;
            Interval = interval;
        }

        public Vector2 Position { get; }

        public Vector2 Trigger { get; }

        public float Radius { get; }

        public int Interval { get; }

        public Queue<KnownObject> Queue { get; init; } = [];

        public Vector2[] Route { get; init; } = [];

        public KnownObject Built { get; init; }

        public List<CombatUnit> Alive { get; } = [];

        public bool Triggered { get; set; }

        public bool Destroyed { get; set; }

        public int NextBuild { get; set; }
    }

    private readonly record struct Pickup(Vector2 Position, KnownObject Id, int Cell);

    private readonly record struct Flight(CombatKind Payload, CombatUnit Shooter, Vector2 To, CombatUnit? Target, bool Lands, int Fired, int Arrives);
}
