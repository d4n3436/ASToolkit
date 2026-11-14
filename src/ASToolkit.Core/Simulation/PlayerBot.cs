using System.Numerics;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Simulation;

internal sealed class PlayerBot
{
    private const int Pistols = PlayerInventory.Pistols;
    private const int WakeInterval = 80;
    private const int DecisionInterval = 16;
    private const float FloodReach = 2000;
    private const float BreakerReach = 40;
    private const float BackOffReach = 140;
    private const float StandOffMargin = 100;
    private const int CrowdSize = 5;
    private const int MaxChargedGunners = 3;
    private const int CrowdEscapeSteps = 25;
    private const int EscapeSteps = 10;
    private const float DetourReach = 400;
    private const int DetourSlack = 5000;
    private const int BlockedMovementTime = 80;
    private const int NavigationPatience = 2000;
    private const int SteerPatience = 500;
    private const int PathLookahead = 16;
    private const float SteeringReach = 48;
    private const float BlastSafetyReach = 200;
    private const int BlastEscapeSteps = 30;
    private const int HuntInterval = 500;
    private const float BreachReach = 160;
    private const float BlastMargin = 80;
    private const float ExplosivePenalty = 400;
    private const float SideStep = 48;
    private const float SecretReach = 300;
    private const float StuckReach = 512;
    private const int CompassSides = 8;
    private const float DodgeWeight = 0.5f;
    private const float DodgeReach = 700;
    private const float CannonKeep = 90;
    private const int CannonPatience = 6000;
    private const float CannonSeekReach = 900;
    private const float PlantReach = 1000;
    private const float PlantSightReach = 2 * PlantReach;
    private const int CannonRest = 8000;
    private const float CannonApproach = 66;
    private const float IdleCursorReach = 200;
    private const float CannonStep = 24;
    private const float DodgeHorizon = 600;
    private const float DodgeStep = 50;
    private const float ThreatReach = 600;
    private const float CoverBonus = 200;
    private const float DistantDangerShare = 0.25f;
    private const float TieBreak = 1e-6f;
    private const float SideStepGrain = 4;
    private const float SideStepClearance = 16;
    private const float SideStepAim = 128;
    private const int MinPocketCells = 16;
    private const int BreachPatience = 20000;
    private const int SecretInterval = 1000;
    private const float GunnerKeep = 0.6f;
    private readonly GameWorld _world;
    private readonly RouteAnalysis _analysis;
    private readonly WalkField _field;
    private readonly Gunnery _gunnery;
    private readonly List<Unit> _threats = [];
    private readonly List<Unit> _explosives = [];
    private readonly List<Unit> _chained = [];
    private readonly List<Unit> _cluster = [];
    private readonly Dictionary<WorldPickup, (Vector2 Position, int Cell, int Collected)> _pickupCells = [];
    private readonly HashSet<WorldPickup> _unreachablePickups = [];
    private WorldPickup? _detourTarget;
    private int _detourCell = -1;
    private Vector2 _detourEnd;
    private int _weapon = Pistols;
    private bool _chargesGunners;
    private readonly HashSet<Unit> _unreachableCrates = [];
    private Unit? _detourCrate;
    private readonly List<Vector2> _way = [];
    private readonly Queue<Vector2> _path = [];
    private readonly Vector2[] _tour;
    private readonly Vector2[] _tourTargets;
    private readonly float[] _tourProgress;
    private Unit? _aimTarget;
    private Unit? _breachWall;
    private int _breachSince;
    private int _nextSecretAt;
    private Unit? _worstThreat;
    private Vector2? _playerGoal;
    private int _cannonIdleSince;
    private bool _seeksCannon;
    private bool _planting;
    private Vector2 _aimCursor;
    private int _pathEnds;
    private bool _offTour;
    private int _blockedMovementTime;
    private Vector2 _navigationPosition;
    private int _navigationTime;
    private Vector2 _progressPosition;
    private int _progressTime;
    private bool _recovering;
    private int _cursor;
    private int _farthest;
    private int _nextHuntAt;
    private bool _evadingBlast;
    private readonly List<Unit> _incoming = [];
    private int _floodCell = -1;
    private readonly List<TerrainChange> _terrainChanges = [];
    private int _lives;
    private bool _lastDoorsLocked;
    private bool _lastFencesUp;
    private PlayerCommand _command;

    public PlayerBot(GameWorld world, RouteAnalysis analysis, WalkField field)
    {
        _world = world;
        _analysis = analysis;
        _field = field;
        _gunnery = new Gunnery(world, field.Obstacles);
        _aimCursor = PlayerCommand.GetCursor(world.Player.Position);
        _lastDoorsLocked = world.DoorsLocked;
        _lastFencesUp = world.FencesUp;
        _lives = world.Inventory.Items.GetValueOrDefault(KnownObject.ExtraLife);
        var lattice = analysis.Grid.Lattice;
        _tour = analysis.Tour.Select(cell => lattice.GetCenter(cell).ToVector2()).ToArray();
        _tourTargets = (Vector2[])_tour.Clone();
        _tourProgress = new float[_tour.Length];
        for (int index = 1; index < _tour.Length; index++)
        {
            _tourProgress[index] = _tourProgress[index - 1] + Vector2.Distance(_tour[index], _tour[index - 1]);
        }

        float total = _tourProgress.Length > 0 ? Math.Max(1, _tourProgress[^1]) : 1;
        for (int index = 0; index < _tourProgress.Length; index++)
        {
            _tourProgress[index] /= total;
        }

        for (int index = 0; index < _tour.Length; index++)
        {
            int free = _field.FindFree(_tour[index]);

            if (free >= 0)
            {
                _tour[index] = _field.GetCenter(free);
            }
        }
    }

    public bool HasFinished => _tour.Length > 1 && _cursor == _tour.Length - 1;

    public bool CanContinue => _cursor < _tour.Length - 1;

    public float FarthestProgress => GetProgress(_farthest);

    private void SynchronizeWorld()
    {
        int lives = _world.Inventory.Items.GetValueOrDefault(KnownObject.ExtraLife);
        bool respawned = lives < _lives;
        _lives = lives;

        if (respawned)
        {
            _path.Clear();
            _offTour = true;
            _recovering = false;
            _aimTarget = null;
            _worstThreat = null;
            _detourTarget = null;
            _detourCrate = null;
            _detourCell = -1;
            _navigationTime = _world.Time;
            _progressTime = _world.Time;
        }

        _field.Obstacles.Synchronize(_world, _terrainChanges);
        bool changed = _lastDoorsLocked != _world.DoorsLocked || _lastFencesUp != _world.FencesUp || _terrainChanges.Count > 0;
        foreach (var change in _terrainChanges)
        {
            _field.RefreshMovement(change.Position, change.HalfSize);
        }

        _terrainChanges.Clear();

        if (!changed)
            return;

        _unreachablePickups.Clear();
        _unreachableCrates.Clear();
        _lastDoorsLocked = _world.DoorsLocked;
        _lastFencesUp = _world.FencesUp;
        _field.SetGates(_lastDoorsLocked, _lastFencesUp);
        _path.Clear();
        _floodCell = -1;
        _offTour = true;
    }

    private (Vector2 Position, int Cell, int Collected) GetPickupSpot(WorldPickup pickup)
    {
        if (!_pickupCells.TryGetValue(pickup, out var cached) ||
            pickup.IsDropped && Vector2.DistanceSquared(cached.Position, pickup.Position.AsVector2()) >= _field.CellStep * _field.CellStep)
        {
            var position = pickup.Position.AsVector2();
            int collected = -1;
            for (int index = 0; index < _tour.Length; index++)
            {
                if (_tour[index].MeasureDistance(position) < GameWorld.GetPickupReach(pickup.Id))
                {
                    collected = index;
                }
            }

            cached = (position, _field.FindFree(position), collected);
            _pickupCells[pickup] = cached;
        }

        return cached;
    }

    public void PlayTick()
    {
        _world.TakePickups();
        if (_world.Time % DecisionInterval < _world.FrameTime)
        {
            _command = PlanCommand();
        }

        var command = _command;
        var dodge = _world.IsInCannon ? null : FindDodge(command.Destination) ?? (_seeksCannon ? null : SkirtCannon(command.Destination));

        if (dodge is not null)
        {
            command = command with { Destination = dodge };
        }

        var previous = _world.Player.Position.AsVector2();
        _world.ApplyPlayerCommand(command);
        ObserveMovement(previous);
        _world.Update();
    }

    private PlayerCommand PlanCommand()
    {
        _playerGoal = null;
        SynchronizeWorld();

        if (IsWakeTick || _aimTarget is not null && !_aimTarget.IsAlive)
        {
            FindTargets();
        }

        var target = _aimTarget ?? (_chained.Count == 0 ? FindBreach() ?? FindLootCrate() : null);
        var threat = _worstThreat is not null && _worstThreat.IsAlive ? _worstThreat : null;
        float worstIntrusion = threat is null ? float.MinValue : GetKeptDistance(threat.Kind) - _world.Player.Position.AsVector2().MeasureDistance(threat.Position.AsVector2());

        if (target is not null && _world.Weapons.CanFire)
        {
            _weapon = target == _aimTarget || target == _breachWall ? _gunnery.SelectWeapon(target) : Pistols;
        }

        int weapon = _weapon;

        Vector2? danger = null;
        foreach (var explosive in _world.ArmedDynamite)
        {
            if (_world.Player.Position.AsVector2().MeasureDistance(explosive.Position.AsVector2()) < BlastSafetyReach)
            {
                danger = explosive.Position.AsVector2();
                break;
            }
        }

        if (danger is null)
        {
            _evadingBlast = false;
        }

        if (_world.IsInCannon)
        {
            _seeksCannon = false;
            ManCannon();
        }
        else if (danger is not null)
        {
            if (!_evadingBlast || _path.Count == 0 || IsWakeTick)
            {
                _evadingBlast = PlanBlastEscape(danger.Value);
            }

            FollowPath();
        }
        else if (SeekCannon())
        {
            FollowPath();
        }
        else if (_recovering)
        {
            FollowPath();
            _recovering = _path.Count > 0;
        }
        else if (threat is not null && worstIntrusion > 0)
        {
            if (IsWakeTick || _path.Count == 0 || _pathEnds != int.MaxValue)
            {
                PlanEscape();
            }

            if (_path.Count > 0)
            {
                _offTour = true;
                FollowPath();
            }
            else
            {
                Walk();
            }
        }
        else if (!CanContinue)
        {
            if ((_path.Count == 0 || _world.Time > _pathEnds) && _world.Time >= _nextHuntAt)
            {
                PlanHunt();
            }

            FollowPath();
        }
        else if (!PlantNearby())
        {
            if (_offTour)
            {
                if (PlanRejoin())
                {
                    _offTour = false;
                    FollowPath();
                }
            }
            else if (!IsBreaching(target))
            {
                if (_detourCell >= 0 && _path.Count == 0)
                {
                    EndDetour();
                    _offTour = !PlanDetour();
                }
                else if (_path.Count > 0 || threat is null && IsWakeTick && PlanDetour())
                {
                    FollowPath();
                }
                else
                {
                    Walk();
                }
            }
        }

        if (target is not null)
        {
            _aimCursor = PlayerCommand.GetCursor(_gunnery.GetAimPoint(target, weapon));
        }
        else if (_playerGoal is not null && _world.Player.Position.AsVector2().MeasureDistance(_playerGoal.Value) >= 1)
        {
            _aimCursor = PlayerCommand.GetCursor(_world.Player.Position) + Vector2.Normalize(_playerGoal.Value - _world.Player.Position.AsVector2()) * IdleCursorReach;
        }

        return new PlayerCommand(_playerGoal, _aimCursor, target is not null, weapon);
    }

    private void ObserveMovement(Vector2 previous)
    {
        _blockedMovementTime = _playerGoal is not null && _world.Player.Position.AsVector2() == previous ? _blockedMovementTime + _world.FrameTime : 0;

        if (_playerGoal is null || Vector2.DistanceSquared(_world.Player.Position.AsVector2(), _navigationPosition) >= _world.Player.Kind.Object.Size.X * _world.Player.Kind.Object.Size.X)
        {
            _navigationPosition = _world.Player.Position.AsVector2();
            _navigationTime = _world.Time;
        }

        if (_playerGoal is null || Vector2.DistanceSquared(_world.Player.Position.AsVector2(), _progressPosition) >= _world.Player.Kind.Object.Size.X * _world.Player.Kind.Object.Size.X)
        {
            _progressPosition = _world.Player.Position.AsVector2();
            _progressTime = _world.Time;
        }

        if (_blockedMovementTime >= BlockedMovementTime || _world.Time - _navigationTime >= NavigationPatience)
        {
            bool stalled = _world.Time - _navigationTime >= NavigationPatience;

            if (stalled && _detourCell >= 0)
            {
                EndDetour();
            }

            _path.Clear();
            _offTour = true;
            _recovering = stalled && PlanRecovery();
            _blockedMovementTime = 0;

            if (stalled)
            {
                _navigationPosition = _world.Player.Position.AsVector2();
                _navigationTime = _world.Time;
            }
        }
    }

    private void FindTargets()
    {
        FindChainedExplosives();
        _threats.Clear();
        Unit? target = null;
        Unit? threat = null;
        float targetPriority = float.MinValue;
        float worstIntrusion = float.MinValue;
        var reach = Vector2.Max(Gunnery.ScreenHalf, new Vector2(ThreatReach));
        int gunners = 0;
        foreach (var monster in _world.Units.GetNear(_world.Player.Position.AsVector2() - reach, _world.Player.Position.AsVector2() + reach))
        {
            if (monster is { Army: GameWorld.MonsterArmy, IsAlive: true } && monster.Goal == _world.Player && monster.Kind.BattleRange > BackOffReach)
            {
                gunners++;
            }
        }

        _chargesGunners = gunners <= MaxChargedGunners && _gunnery.HoldsShortReach();
        foreach (var monster in _world.Units.GetNear(_world.Player.Position.AsVector2() - reach, _world.Player.Position.AsVector2() + reach))
        {
            if (!monster.IsAlive)
                continue;

            float distance = _world.Player.Position.AsVector2().MeasureDistance(monster.Position.AsVector2());
            float intrusion = GetKeptDistance(monster.Kind) - distance;
            bool onScreen = _gunnery.IsOnScreen(monster.Position.AsVector2());
            float priority = MeasurePriority(monster, distance);
            bool isCloser = onScreen && priority > targetPriority;
            bool threatens = monster.Goal == _world.Player && intrusion > worstIntrusion - StandOffMargin;

            bool isGenerator = _world.FencesUp && monster.Kind.Id == KnownObject.Generator && monster.IsAlive;

            // Count attackers off screen as threats only
            if (monster == _world.Player || (monster.Army != GameWorld.MonsterArmy && !isGenerator) || (monster.Placement is not null && !isGenerator) ||
                !onScreen && (distance > monster.Kind.BattleRange + StandOffMargin || !threatens) || (!isCloser && !threatens) || !_gunnery.HasClearAim(monster))
                continue;

            if (isCloser)
            {
                target = monster;
                targetPriority = priority;
            }

            if (monster.Goal == _world.Player && intrusion > -StandOffMargin)
            {
                _threats.Add(monster);
            }

            if (monster.Goal == _world.Player && intrusion > worstIntrusion)
            {
                threat = monster;
                worstIntrusion = intrusion;
            }
        }

        _aimTarget = target;
        _worstThreat = threat;
    }

    // Ranks a target by the damage a second it deals against its health, a monster out of reach counting at a share
    private float MeasurePriority(Unit monster, float distance)
    {
        var profile = _gunnery.Attacks.Get(monster.Kind);
        float danger = profile.Damage * 1000f / Math.Max(_world.FrameTime, monster.Kind.AttackTime);

        if (distance > monster.Kind.BattleRange + StandOffMargin)
        {
            danger *= DistantDangerShare;
        }

        return danger / Math.Max(1, monster.Hp) - distance * TieBreak;
    }

    private Unit? FindLootCrate()
    {

        Unit? best = null;
        float nearest = float.MaxValue;
        foreach (var unit in _world.Units.GetNear(_world.Player.Position.AsVector2() - Gunnery.ScreenHalf, _world.Player.Position.AsVector2() + Gunnery.ScreenHalf))
        {
            if (unit.Kind.Id != KnownObject.Crate || unit.DropItem == 0 || !IsWanted(unit.DropItem) ||
                !_gunnery.IsOnScreen(unit.Position.AsVector2()) || !_gunnery.HasClearAim(unit))
                continue;

            float distance = Vector2.DistanceSquared(_world.Player.Position.AsVector2(), unit.Position.AsVector2());

            if (distance < nearest)
            {
                nearest = distance;
                best = unit;
            }
        }

        return best;
    }

    private int FindBestReached(CellScore measure, float floor, Vector2 point = default)
    {
        int best = -1;
        for (int order = 0; order < _field.ReachedCount; order++)
        {
            int cell = _field.GetReached(order);
            var center = _field.GetCenter(cell);
            float score = measure switch
            {
                CellScore.AwayFrom => Vector2.DistanceSquared(center, point),
                CellScore.Escape => MeasureEscape(center),
                _ => MeasureForward(center)
            };

            if (score > floor)
            {
                floor = score;
                best = cell;
            }
        }

        return best;
    }

    private bool PlanBlastEscape(Vector2 explosive)
    {
        FloodPlayer(BlastEscapeSteps);
        int best = FindBestReached(CellScore.AwayFrom, 0, explosive);
        _offTour = true;

        return QueueCell(best);
    }

    private bool PlanHunt()
    {
        _nextHuntAt = _world.Time + HuntInterval;
        FloodPlayer(int.MaxValue);

        if (_analysis.Exit is not null)
        {
            var exit = _analysis.Exit.Value;

            if (QueueTrigger(exit.Center.ToVector2(), exit.Radius))
                return true;
        }

        if (PlanBreaker() || PlanGenerator() || PlanDynamite())
            return true;

        int goal = -1;
        int nearest = int.MaxValue;
        foreach (var unit in _world.Monsters)
        {
            if (!unit.IsAlive || unit.Army != GameWorld.MonsterArmy)
                continue;

            int cell = _field.Find(unit.Position.AsVector2());

            if (cell < 0 || _field.GetDistance(cell) < 0)
            {
                cell = _field.FindFree(unit.Position.AsVector2(), reachedOnly: true);
            }

            if (cell < 0 || _field.GetDistance(cell) >= nearest)
                continue;

            goal = cell;
            nearest = _field.GetDistance(cell);
        }

        return QueueCell(goal);
    }

    private bool PlanRejoin()
    {
        FloodPlayer(int.MaxValue);

        // Step toward the nearest standable cell outside the player's pocket while no straight walk leaves it
        if (_field.ReachedCount < MinPocketCells)
        {
            int nearest = _field.FindFree(_world.Player.Position.AsVector2(), StuckReach, skipReached: true, skippedHalf: _world.Player.Kind.Half + PlayerMovement.TargetHalf);

            if (nearest >= 0)
            {
                _playerGoal = SteerClear(_field.GetCenter(nearest));
            }

            return false;
        }

        if (_detourCell >= 0 && IsDetourWanted() && QueuePath(_detourCell, _detourEnd))
            return true;

        if (PlanBreaker() || PlanDynamite())
            return true;

        int index = _cursor;
        while (index < _tourTargets.Length - 1 && _field.Find(_tourTargets[index]) < 0)
        {
            index++;
        }

        for (int ahead = index; ahead < _tourTargets.Length; ahead++)
        {
            int goal = _field.FindFree(_tourTargets[ahead], reachedOnly: true);

            if (goal < 0 || _world.PlayerMovement.HasReached(_world.Player, _field.GetCenter(goal)))
                continue;

            _cursor = ahead;
            _farthest = Math.Max(_farthest, _cursor);

            return QueuePath(goal, _tour[_cursor] = _field.GetCenter(goal));
        }

        if (PlanBreach())
            return true;

        _cursor = _tour.Length - 1;

        return false;
    }

    private bool PlanBreach()
    {
        var player = _world.Player.Position.AsVector2();
        var ahead = _tourTargets[Math.Min(_cursor + 1, _tourTargets.Length - 1)];
        _breachWall = null;
        int goal = -1;
        float best = float.MaxValue;
        foreach (var body in _world.Bodies)
        {
            if (!body.IsAlive || body.Kind.Id is not (KnownObject.BreakableWall or KnownObject.BreakableWall2))
                continue;

            var wall = body.Position.AsVector2();
            float score = Vector2.Distance(player, wall) + Vector2.Distance(wall, ahead);
            int cell = score < best ? _field.FindFree(wall, BreachReach, reachedOnly: true) : -1;

            if (cell < 0)
                continue;

            _breachWall = body;
            goal = cell;
            best = score;
        }

        if (_breachWall is null)
            return false;

        int spot = FindFiringSpot(_breachWall);

        return QueuePath(spot >= 0 ? spot : goal, _field.GetCenter(spot >= 0 ? spot : goal));
    }

    private int FindFiringSpot(Unit body)
    {
        var position = body.Position.AsVector2();
        CollectChain(position, body.Kind.Half, _cluster);
        for (int order = 0; order < _field.ReachedCount; order++)
        {
            int cell = _field.GetReached(order);
            var center = _field.GetCenter(cell);
            var offset = Vector2.Abs(center - position);

            if (offset.X >= Gunnery.ScreenHalf.X || offset.Y >= Gunnery.ScreenHalf.Y ||
                ReachesAny(_cluster, center, _world.Player.Kind.Half + new Vector2(BlastMargin)) ||
                !_gunnery.HasClearAim(body, center))
                continue;

            return cell;
        }

        return -1;
    }

    private bool IsWakeTick => _world.Time % WakeInterval < _world.FrameTime;

    private bool IsNewWeapon(KnownObject id) =>
        id is >= KnownObject.Shotgun and <= KnownObject.MagmaMinigun && !_world.Inventory.HoldsWeapon((int)id - PlayerInventory.WeaponPickupBase);

    private static int DiscountWeapon(int steps) => steps > 0 ? Math.Max(1, (int)(steps * DetourReach / FloodReach)) : steps;

    private bool IsWanted(KnownObject id)
    {
        int slot = PlayerInventory.GetAmmoSlot(id);

        return _world.Inventory.CanTakePickup(id) && (slot < 0 || _world.Inventory.HoldsWeapon(slot));
    }

    private Unit? FindBreach() =>
        _breachWall is not null && _breachWall.IsAlive && _gunnery.IsOnScreen(_breachWall.Position.AsVector2()) && _gunnery.HasClearAim(_breachWall)
            ? _breachWall : null;

    private bool PlanBreaker() =>
        _world.DoorsLocked && _analysis.Objectives.Any(objective => objective.Kind == ObjectiveKind.Breaker && QueueTrigger(new Vector2(objective.X, objective.Y), BreakerReach));

    private bool PlanGenerator()
    {
        if (!_world.FencesUp)
            return false;

        foreach (var body in _world.Bodies)
        {
            if (body.IsAlive && body.Kind.Id == KnownObject.Generator && QueueTrigger(body.Position.AsVector2(), BreakerReach))
                return true;
        }

        return false;
    }

    private bool PlanDynamite()
    {
        bool waits = false;
        foreach (var site in _world.DynamiteSites)
        {
            waits |= IsReachedOnTour(site.Position.AsVector2());
        }

        if (!waits)
            return false;

        if (CarriesDynamite)
        {
            foreach (var site in _world.DynamiteSites)
            {
                var point = site.Position.AsVector2();

                if (IsReachedOnTour(point) && QueueTrigger(point, BreakerReach))
                    return true;
            }

            return false;
        }

        foreach (var pickup in _world.Pickups)
        {
            int cell = pickup.Id == KnownObject.Dynamite ? GetPickupSpot(pickup).Cell : -1;

            if (cell >= 0 && _field.GetDistance(cell) >= 0 && QueuePath(cell, pickup.Position.AsVector2()))
                return true;
        }

        return false;
    }

    private bool CarriesDynamite => _world.Inventory.Items.GetValueOrDefault(KnownObject.Dynamite) > 0;

    private bool PlantNearby()
    {
        if (_world.DynamiteSites.Count == 0)
            return _planting = false;

        if ((!_planting || _path.Count == 0 || IsWakeTick) && !HasPlantTargetNear())
        {
            _planting = false;
        }
        else if (!_planting || _path.Count == 0 || IsWakeTick)
        {
            FloodWithin(PlantReach);
            int best = -1;
            int nearest = int.MaxValue;
            var end = Vector2.Zero;
            bool carries = CarriesDynamite;
            if (carries)
            {
                foreach (var site in _world.DynamiteSites)
                {
                    Consider(_field.FindTrigger(site.Position.AsVector2(), BreakerReach), site.Position.AsVector2());
                }
            }
            else
            {
                foreach (var pickup in _world.Pickups)
                {
                    if (pickup.Id == KnownObject.Dynamite)
                    {
                        Consider(GetPickupSpot(pickup).Cell, pickup.Position.AsVector2());
                    }
                }
            }

            _planting = best >= 0 && QueuePath(best, end, approachEnd: carries);

            void Consider(int cell, Vector2 point)
            {
                int steps = cell >= 0 ? _field.GetDistance(cell) : -1;

                if (steps >= 0 && steps < nearest)
                {
                    best = cell;
                    nearest = steps;
                    end = point;
                }
            }
        }

        if (_planting)
        {
            FollowPath();
        }

        return _planting;
    }

    private bool HasPlantTargetNear()
    {
        var player = _world.Player.Position.AsVector2();
        if (CarriesDynamite)
        {
            foreach (var site in _world.DynamiteSites)
            {
                if (player.MeasureDistance(site.Position.AsVector2()) < PlantSightReach)
                    return true;
            }

            return false;
        }

        foreach (var pickup in _world.Pickups)
        {
            if (pickup.Id == KnownObject.Dynamite && player.MeasureDistance(pickup.Position.AsVector2()) < PlantSightReach)
                return true;
        }

        return false;
    }

    private bool IsReachedOnTour(Vector2 site)
    {
        if (HasFinished)
            return true;

        RouteObjective? nearest = null;
        float best = float.MaxValue;
        foreach (var objective in _analysis.Objectives)
        {
            float distance = Vector2.DistanceSquared(new Vector2(objective.X, objective.Y), site);

            if (objective.Kind == ObjectiveKind.DynamiteSite && distance < best)
            {
                nearest = objective;
                best = distance;
            }
        }

        return nearest is null || nearest.Progress <= FarthestProgress;
    }

    private bool PlanRecovery()
    {
        FloodPlayer(EscapeSteps);

        return QueueCell(FindBestReached(CellScore.Forward, 0));
    }

    private float MeasureForward(Vector2 center)
    {
        var offset = center - _world.Player.Position.AsVector2();
        float reach = _world.Player.Kind.Object.Size.X * 2;

        return offset.LengthSquared() <= reach * reach && _field.Obstacles.CanMove(_world.Player.Kind.Object, _world.Player.Position, center)
            ? Vector2.Dot(offset, _world.Player.Facing.Direction) : float.MinValue;
    }

    private static Vector2 GetCompassStep(int side)
    {
        float angle = side * MathF.Tau / CompassSides;

        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }

    private bool StandsClear(Vector3 start, Vector2 step)
    {
        for (float distance = SideStepGrain; distance <= SideStepClearance; distance += SideStepGrain)
        {
            if (!_field.Obstacles.CanStand(_world.Player.Kind.Object, start + (step * distance).AsVector3()))
                return false;
        }

        return true;
    }

    private bool MovesClear(Vector3 start, Vector2 step, float length) =>
        _field.Obstacles.CanMove(_world.Player.Kind.Object, start, start.AsVector2() + step * length);

    private void FindChainedExplosives() =>
        CollectChain(_world.Player.Position.AsVector2(), _world.Player.Kind.Half + new Vector2(BlastMargin), _chained);

    private void CollectChain(Vector2 point, Vector2 half, List<Unit> chain)
    {
        _explosives.Clear();
        chain.Clear();
        foreach (var body in _world.Units.GetNear(point - Gunnery.ScreenHalf, point + Gunnery.ScreenHalf))
        {
            if (body.Placement is not null && body is { IsAlive: true, Kind.Damage: > 0 })
            {
                _explosives.Add(body);
            }
        }

        foreach (var body in _explosives)
        {
            if (Reaches(body, point, half))
            {
                chain.Add(body);
            }
        }

        for (int index = 0; index < chain.Count; index++)
        {
            foreach (var body in _explosives)
            {
                if (!chain.Contains(body) && Reaches(body, chain[index].Position.AsVector2(), chain[index].Kind.Half))
                {
                    chain.Add(body);
                }
            }
        }
    }

    private static bool ReachesAny(List<Unit> bodies, Vector2 point, Vector2 half)
    {
        foreach (var body in bodies)
        {
            if (Reaches(body, point, half))
                return true;
        }

        return false;
    }

    private static bool Reaches(Unit body, Vector2 point, Vector2 half)
    {
        var reach = body.Kind.Half + new Vector2(body.Kind.BlastRadius) + half;
        var offset = Vector2.Abs(body.Position.AsVector2() - point);

        return offset.X < reach.X && offset.Y < reach.Y;
    }

    // Keeps no distance while protected, and a melee distance from gunners other than tank gunners while a short-reach weapon is held and few gunners shoot
    private float GetKeptDistance(ObjectDefinition kind)
    {
        if (_world.IsProtected)
            return 0;

        if (kind.BattleRange <= BackOffReach)
            return MathF.Max(BackOffReach, kind.Half.Length() * 2);

        if (_chargesGunners && kind.Id is not (KnownObject.TankWeaponMonster or KnownObject.YellowTankWeaponMonster or KnownObject.RedTankWeaponMonster))
            return BackOffReach;

        return MathF.Max(BackOffReach, (kind.BattleRange + StandOffMargin) * GunnerKeep);
    }

    private void PlanEscape()
    {
        _path.Clear();
        int start = FloodPlayer(_threats.Count >= CrowdSize ? CrowdEscapeSteps : EscapeSteps);
        _floodCell = -1;
        int best = FindBestReached(CellScore.Escape, float.MinValue);

        if (best != start)
        {
            QueuePath(best, _field.GetCenter(best));
            _pathEnds = int.MaxValue;
        }
    }

    private float MeasureEscape(Vector2 center)
    {
        float score = float.MaxValue;
        float offLine = float.MaxValue;
        bool covered = true;
        foreach (var threat in _threats)
        {
            float distance = center.MeasureDistance(threat.Position.AsVector2());
            score = MathF.Min(score, distance - GetKeptDistance(threat.Kind));

            if (threat.Kind.BattleRange <= BackOffReach)
                continue;

            offLine = MathF.Min(offLine, MeasureOffLine(threat.Position.AsVector2(), center));
            covered = covered && (distance > threat.Kind.BattleRange || !_gunnery.HasClearAim(threat, center));
        }

        if (offLine < float.MaxValue)
        {
            score += DodgeWeight * offLine + (covered ? CoverBonus : 0);
        }

        return ReachesAny(_explosives, center, _world.Player.Kind.Half) ? score - ExplosivePenalty : score;
    }

    private Vector2? FindDodge(Vector2? goal)
    {
        _incoming.Clear();
        var center = _world.Player.Position.AsVector2();
        foreach (var round in _world.Effects.Active)
        {
            if (round.Shooter is null || round.Shooter.Army == _world.Player.Army || round.Speed <= 0 || round.Speed >= ObjectDefinition.Unlimited ||
                round.Kind.Damage <= 0 || center.MeasureDistance(round.Position.AsVector2()) > DodgeReach)
                continue;

            _incoming.Add(round);
        }

        if (_incoming.Count == 0)
            return null;

        var planned = goal is null ? Vector2.Zero : goal.Value - center;
        planned = planned.LengthSquared() < 1 ? Vector2.Zero : Vector2.Normalize(planned);
        float plannedHit = MeasureFirstHit(planned);

        if (plannedHit >= DodgeHorizon)
            return null;

        float bestHit = plannedHit;
        Vector2? best = null;
        for (int side = 0; side < CompassSides; side++)
        {
            var step = GetCompassStep(side);

            if (!StandsClear(_world.Player.Position, step))
                continue;

            float hit = MeasureFirstHit(step);

            if (hit > bestHit || hit >= DodgeHorizon && bestHit >= DodgeHorizon && best is not null && Vector2.Dot(step, planned) > Vector2.Dot(best.Value, planned))
            {
                bestHit = hit;
                best = step;
            }
        }

        return best is null ? null : center + best.Value * SideStepAim;
    }

    private float MeasureFirstHit(Vector2 direction)
    {
        var start = _world.Player.Position.AsVector2();
        float speed = _world.Inventory.Speed / 1000f;
        float first = DodgeHorizon;
        foreach (var round in _incoming)
        {
            var velocity = round.Facing.Direction * round.Speed;
            var reach = _world.Player.Kind.Half + round.Kind.Half + new Vector2(round.Kind.BlastRadius);
            for (float time = 0; time < first; time += DodgeStep)
            {
                var offset = Vector2.Abs(start + direction * speed * time - (round.Position.AsVector2() + velocity * time));

                if (offset.X < reach.X && offset.Y < reach.Y)
                {
                    first = time;
                    break;
                }
            }
        }

        return first;
    }

    private bool SeekCannon()
    {
        if (_aimTarget is null || _aimTarget.Army != GameWorld.MonsterArmy || _world.Time - _cannonIdleSince < CannonRest || CountMonstersOnScreen() < CrowdSize)
        {
            _seeksCannon = false;

            return false;
        }

        if (_seeksCannon && _path.Count > 0)
            return true;

        var center = _world.Player.Position.AsVector2();
        foreach (var cannon in _world.Scripted)
        {
            var position = cannon.Position.AsVector2();

            if (!IsParked(cannon) || center.MeasureDistance(position) > CannonSeekReach)
                continue;

            FloodWithin(CannonSeekReach * 2);

            if (QueueTrigger(position, CannonApproach))
                return _seeksCannon = true;
        }

        return _seeksCannon = false;
    }

    private void ManCannon()
    {
        if (_aimTarget is not null && _aimTarget.Army == GameWorld.MonsterArmy && CountMonstersOnScreen() >= CrowdSize)
        {
            _cannonIdleSince = _world.Time;
        }
        else if (_world.Time - _cannonIdleSince > CannonPatience)
        {
            _playerGoal = _tour[Math.Min(_cursor + 1, _tour.Length - 1)];
        }
    }

    private int CountMonstersOnScreen()
    {
        int count = 0;
        var center = _world.Player.Position.AsVector2();
        foreach (var unit in _world.Units.GetNear(center - Gunnery.ScreenHalf, center + Gunnery.ScreenHalf))
        {
            if (unit is { IsAlive: true, Army: GameWorld.MonsterArmy, Placement: null })
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsParked(Unit cannon) => cannon.Kind.Id == KnownObject.MountedCannon && cannon.Animation < SpriteAnimation.Death;

    // Counts a tour point inside a parked cannon's reach as passed once the player skirts the cannon beside it
    private bool IsBesideCannon(Vector2 point)
    {
        foreach (var cannon in _world.Scripted)
        {
            var position = cannon.Position.AsVector2();

            if (IsParked(cannon) && point.MeasureDistance(position) < CannonKeep &&
                _world.Player.Position.AsVector2().MeasureDistance(position) < CannonKeep + CannonStep * 2)
                return true;
        }

        return false;
    }

    private Vector2? SkirtCannon(Vector2? goal)
    {
        if (goal is null)
            return null;

        var center = _world.Player.Position.AsVector2();
        var heading = goal.Value - center;

        if (heading.LengthSquared() < 1)
            return null;

        heading = Vector2.Normalize(heading);
        foreach (var cannon in _world.Scripted)
        {
            if (!IsParked(cannon))
                continue;

            var away = center - cannon.Position.AsVector2();
            var ahead = center + heading * CannonStep - cannon.Position.AsVector2();

            if (ahead.Length() >= CannonKeep || away.LengthSquared() < 1)
                continue;

            var tangent = Vector2.Normalize(new Vector2(-away.Y, away.X));

            if (Vector2.Dot(tangent, heading) < 0)
            {
                tangent = -tangent;
            }

            return center + Vector2.Normalize(tangent + Vector2.Normalize(away) * MathF.Max(0, CannonKeep - away.Length()) / CannonKeep) * SideStepAim;
        }

        return null;
    }

    private float MeasureOffLine(Vector2 gunner, Vector2 spot)
    {
        var line = _world.Player.Position.AsVector2() - gunner;

        if (line.LengthSquared() < 1)
            return 0;

        var side = spot - _world.Player.Position.AsVector2();

        return MathF.Abs(line.X * side.Y - line.Y * side.X) / line.Length();
    }

    private bool PlanSecret(int maxSteps)
    {
        if (_world.Time < _nextSecretAt)
            return false;

        Unit? wall = null;
        int spot = -1;
        int nearest = maxSteps;
        foreach (var element in _analysis.Elements)
        {
            if (element.Kind != ElementKind.SecretWall)
                continue;

            var point = new Vector2(element.X, element.Y);
            Unit? body = null;
            foreach (var candidate in _world.Units.GetNear(point - new Vector2(SideStep), point + new Vector2(SideStep)))
            {
                if (candidate.Placement is not null && candidate is { IsAlive: true, Kind.Id: KnownObject.BreakableWall })
                {
                    body = candidate;
                }
            }

            if (body is null || _unreachableCrates.Contains(body) || !HidesLoot(point))
                continue;

            int cell = FindFiringSpot(body);
            int steps = cell >= 0 ? _field.GetDistance(cell) : -1;

            if (HidesNewWeapon(point))
            {
                steps = DiscountWeapon(steps);
            }

            if (steps < 0 || steps > nearest)
                continue;

            wall = body;
            spot = cell;
            nearest = steps;
        }

        if (wall is null)
        {
            _nextSecretAt = _world.Time + SecretInterval;

            return false;
        }

        if (_breachWall != wall)
        {
            _breachWall = wall;
            _breachSince = _world.Time;
        }

        return StartDetour(spot, _field.GetCenter(spot));

        bool HidesNewWeapon(Vector2 point)
        {
            foreach (var pickup in _world.Pickups)
            {
                if (Vector2.Distance(pickup.Position.AsVector2(), point) < SecretReach && IsNewWeapon(pickup.Id))
                    return true;
            }

            return false;
        }

        bool HidesLoot(Vector2 point)
        {
            foreach (var pickup in _world.Pickups)
            {
                if (Vector2.Distance(pickup.Position.AsVector2(), point) < SecretReach && IsWanted(pickup.Id) &&
                    _field.FindFree(pickup.Position.AsVector2(), reachedOnly: true) < 0)
                    return true;
            }

            return false;
        }
    }

    private bool PlanDetour()
    {
        FloodWithin(FloodReach);
        int best = -1;
        int bestCell = -1;
        int bestSteps = (int)(DetourReach / _field.CellStep);
        var pickups = _world.Pickups;
        for (int index = 0; index < pickups.Length; index++)
        {
            var pickup = pickups[index];

            if (_unreachablePickups.Contains(pickup))
                continue;

            (_, int cell, int collected) = GetPickupSpot(pickup);
            bool counts = cell >= 0 && (collected <= _cursor || pickup.Placement is null);

            if (counts && _field.GetDistance(cell) < 0)
            {
                cell = _field.FindFree(pickup.Position.AsVector2(), GameWorld.GetPickupReach(pickup.Id) * 2, reachedOnly: true);
            }

            int steps = counts && cell >= 0 ? _field.GetDistance(cell) : -1;

            if (IsNewWeapon(pickup.Id))
            {
                steps = DiscountWeapon(steps);
            }

            if (steps > 0 && steps <= bestSteps && _field.GetCenter(cell).MeasureDistance(pickup.Position.AsVector2()) < GameWorld.GetPickupReach(pickup.Id) * 2 &&
                IsWanted(pickup.Id))
            {
                best = index;
                bestCell = cell;
                bestSteps = steps;
            }
        }

        Unit? crate = null;
        int crateCell = -1;
        foreach (var body in _world.Bodies)
        {
            if (body.Kind.Id != KnownObject.Crate || !body.IsAlive || body.DropItem == 0 || _unreachableCrates.Contains(body) ||
                !IsWanted(body.DropItem))
                continue;

            int cell = _field.FindFree(body.Position.AsVector2(), reachedOnly: true);
            int steps = cell >= 0 ? _field.GetDistance(cell) : -1;

            if (steps > 0 && steps <= bestSteps)
            {
                crate = body;
                crateCell = cell;
                bestSteps = steps;
            }
        }

        if (crate is not null)
        {
            _detourCrate = crate;

            return StartDetour(crateCell, _field.GetCenter(crateCell));
        }

        if (best < 0)
            return PlanSecret(bestSteps);

        _detourTarget = pickups[best];

        return StartDetour(bestCell, _detourTarget.Position.AsVector2());
    }

    private bool IsBreaching(Unit? target) => target is not null && target == _breachWall && _world.Time - _breachSince <= BreachPatience;

    private bool StartDetour(int goal, Vector2 end)
    {
        _detourCell = goal;
        _detourEnd = end;

        return QueuePath(goal, end);
    }

    private bool IsDetourWanted()
    {
        if (_detourCrate is not null)
            return _detourCrate.IsAlive;

        if (_detourTarget is null)
            return _breachWall is not null && _breachWall.IsAlive;

        foreach (var pickup in _world.Pickups)
        {
            if (pickup == _detourTarget)
                return IsWanted(pickup.Id);
        }

        return false;
    }

    private void EndDetour()
    {
        if (_detourTarget is not null && IsDetourWanted())
        {
            _unreachablePickups.Add(_detourTarget);
        }

        if (_detourCrate is not null && _detourCrate.IsAlive)
        {
            _unreachableCrates.Add(_detourCrate);
        }

        if (_detourTarget is null && _detourCrate is null && _breachWall is not null && _breachWall.IsAlive)
        {
            _unreachableCrates.Add(_breachWall);
        }

        _detourCell = -1;
        _detourTarget = null;
        _detourCrate = null;
        _offTour = true;
    }

    private bool QueuePath(int goal, Vector2 end, bool approachEnd = false)
    {
        _path.Clear();

        if (goal < 0 || _field.GetDistance(goal) < 0)
        {
            return false;
        }

        _way.Clear();
        for (int cell = goal; cell >= 0 && _field.GetDistance(cell) > 0; cell = _field.GetDownhill(cell))
        {
            _way.Add(_field.GetCenter(cell));
        }

        if (_floodCell >= 0)
        {
            _path.Enqueue(_field.GetCenter(_floodCell));
        }
        for (int index = _way.Count - 1; index >= 0; index--)
        {
            _path.Enqueue(_way[index]);
        }

        var last = _field.GetCenter(goal);
        _path.Enqueue(approachEnd || _field.CanMove(new Vector3(last, _world.Movement.GetSupportHeight(_world.Player.Kind.Object, last)), end) ? end : last);

        _pathEnds = _world.Time + (int)(_path.Count * _field.CellStep * 1000 / _world.Inventory.Speed) + DetourSlack;

        return true;
    }

    private void FollowPath()
    {
        if (_world.Time > _pathEnds)
        {
            _path.Clear();
        }
        while (_path.Count > 0 && _world.PlayerMovement.HasReached(_world.Player, _path.Peek()))
        {
            _path.Dequeue();
        }

        if (_path.Count > 1 && IsWakeTick)
        {
            var start = _world.Player.Position.AsVector2();
            var previous = start;
            int index = 0;
            int skip = 0;
            foreach (var point in _path)
            {
                if (index >= PathLookahead || Vector2.Dot(point - previous, previous - start) < 0 ||
                    !_field.CanMove(new Vector3(start, _world.Player.Position.Z), point))
                    break;

                skip = index++;
                previous = point;
            }

            while (skip-- > 0)
            {
                _path.Dequeue();
            }
        }

        if (_path.Count > 0)
        {
            var point = _path.Peek();
            var offset = point - _world.Player.Position.AsVector2();
            _playerGoal = SteerClear(_path.Count > 1 ? point + Vector2.Normalize(offset) * SteeringReach : point);
        }
    }

    // The engine's obstacle avoidance swings the facing in place when the first steps toward a goal are blocked at a thin corner
    private Vector2 SteerClear(Vector2 goal)
    {
        var start = _world.Player.Position;
        var toward = goal - start.AsVector2();

        if (toward.LengthSquared() < 1 || _worstThreat is not null || _world.Time - _progressTime < SteerPatience)
            return goal;

        toward = Vector2.Normalize(toward);

        if (MovesClear(start, toward, SideStepClearance))
            return goal;

        var best = FindOpenStep(start, toward, SideStepClearance) ?? FindOpenStep(start, toward, SideStepGrain);

        return best is null ? goal : start.AsVector2() + best.Value * SteeringReach;
    }

    private Vector2? FindOpenStep(Vector3 start, Vector2 toward, float length)
    {
        float bestDot = float.MinValue;
        Vector2? best = null;
        for (int side = 0; side < CompassSides; side++)
        {
            var step = GetCompassStep(side);
            float dot = Vector2.Dot(step, toward);

            if (dot > bestDot && MovesClear(start, step, length))
            {
                bestDot = dot;
                best = step;
            }
        }

        return best;
    }

    private void Walk()
    {
        while (true)
        {
            int next = Math.Min(_cursor + 1, _tour.Length - 1);

            if (!_world.PlayerMovement.HasReached(_world.Player, _tour[next]) && !IsBesideCannon(_tour[next]))
            {
                if (!_field.CanMove(_world.Player.Position, _tour[next]))
                {
                    FloodWithin(FloodReach);
                    int goal = _field.FindFree(_tourTargets[next], reachedOnly: true);

                    if (goal >= 0)
                    {
                        _tour[next] = _field.GetCenter(goal);
                    }

                    if (QueuePath(goal, _tour[next]))
                    {
                        FollowPath();
                    }
                    else
                    {
                        _offTour = true;
                    }

                    return;
                }

                _playerGoal = _tour[next];

                return;
            }

            _cursor = next;

            if (_cursor > _farthest)
            {
                _farthest = _cursor;
            }

            if (_cursor == _tour.Length - 1)
                return;
        }
    }

    private int FloodPlayer(int maxSteps)
    {
        int cell = _field.FindStart(_world.Player.Position);
        _field.Flood(cell, maxSteps);
        _floodCell = cell;

        return cell;
    }

    private void FloodWithin(float reach) => FloodPlayer((int)(reach / _field.CellStep));

    private bool QueueCell(int cell) => cell >= 0 && QueuePath(cell, _field.GetCenter(cell));

    private bool QueueTrigger(Vector2 point, float reach)
    {
        int cell = _field.FindTrigger(point, reach);

        return cell >= 0 && QueuePath(cell, point, approachEnd: true);
    }

    private float GetProgress(int cursor) => _tourProgress.Length < 2 ? 1 : _tourProgress[cursor];

    private enum CellScore
    {
        AwayFrom,
        Escape,
        Forward
    }
}
