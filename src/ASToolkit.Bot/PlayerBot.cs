using System.Numerics;
using ASToolkit.Engine;
using ASToolkit.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Routes;

namespace ASToolkit.Bot;

internal sealed class PlayerBot
{
    private const int Pistols = Sprite.Pistols;
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
    private const float PickupReach = 60;
    private const float AmmoReach = 50;
    private const int BlastEscapeSteps = 30;
    private const float RubbleBlastReach = 100;
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
    private const float CacheReach = 300;
    private const float RejoinWindow = 2000;
    private const int SecretInterval = 1000;
    private const float GunnerKeep = 0.6f;

    private readonly Map _map;
    private readonly RouteAnalysis _analysis;
    private readonly WalkField _field;
    private readonly Gunnery _gunnery;
    private readonly List<Sprite> _threats = [];
    private readonly List<Sprite> _explosives = [];
    private readonly List<Sprite> _chained = [];
    private readonly List<Sprite> _cluster = [];
    private readonly Dictionary<MergedMapSprite, (Vector2 Position, int Cell, int Collected)> _pickupCells = [];
    private readonly HashSet<MergedMapSprite> _unreachablePickups = [];
    private readonly HashSet<Sprite> _unreachableCrates = [];
    private readonly List<Vector2> _way = [];
    private readonly Queue<Vector2> _path = [];
    private readonly Vector2[] _tour;
    private readonly Vector2[] _tourTargets;
    private readonly float[] _tourProgress;
    private readonly Vector2[] _holes;
    private readonly PlayerState _state;
    private readonly PickupTracker _pickups;
    private readonly List<Sprite> _incoming = [];
    private readonly List<(int Slot, Vector2 Position)> _caches = [];
    private readonly InputDriver _input;
    private MergedMapSprite? _detourTarget;
    private int _detourCell = -1;
    private Vector2 _detourEnd;
    private int _weapon = Pistols;
    private bool _isChargingGunners;
    private Sprite? _detourCrate;
    private Sprite? _aimTarget;
    private Sprite? _breachWall;
    private int _breachSince;
    private int _nextSecretAt;
    private Sprite? _worstThreat;
    private Vector2? _playerGoal;
    private Vector2 _rubbleReach;
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
    private int _floodCell = -1;
    private Sprite? _player;
    private bool _lastDoorsLocked;
    private bool _lastFencesUp;
    private PlayerCommand _command;
    private Vector2? _reachedGoal;

    public PlayerBot(Map map, RouteAnalysis analysis, WalkField field)
    {
        _map = map;
        _state = new PlayerState(map);
        _pickups = new PickupTracker(map);
        _analysis = analysis;
        _field = field;
        _gunnery = new Gunnery(map, field);
        _input = new InputDriver(map);
        _lastDoorsLocked = map.AreDoorsLocked;
        _lastFencesUp = map.AreFencesUp;
        _holes = map.Sprites
            .Where(sprite => sprite.Id is KnownObject.SmallMonsterHole or KnownObject.BigMonsterHole)
            .Select(sprite => sprite.Position.AsVector2())
            .ToArray();

        var lattice = analysis.Grid.Lattice;
        _tour = analysis.Tour.Select(cell => lattice.GetCenter(cell).ToVector2()).ToArray();
        _tourTargets = (Vector2[])_tour.Clone();
        _tourProgress = new float[_tour.Length];
        for (int i = 1; i < _tour.Length; i++)
        {
            _tourProgress[i] = _tourProgress[i - 1] + Vector2.Distance(_tour[i], _tour[i - 1]);
        }

        float total = _tourProgress.Length > 0 ? Math.Max(1, _tourProgress[^1]) : 1;
        for (int i = 0; i < _tourProgress.Length; i++)
        {
            _tourProgress[i] /= total;
        }

        for (int i = 0; i < _tour.Length; i++)
        {
            int free = _field.FindFree(_tour[i]);

            if (free >= 0)
            {
                _tour[i] = _field.GetCenter(free);
            }
        }
    }

    private enum CellScore
    {
        AwayFrom,
        Escape,
        Forward
    }

    public bool HasFinished => _tour.Length > 1 && _cursor == _tour.Length - 1;

    public bool CanContinue => _cursor < _tour.Length - 1;

    public float FarthestProgress => GetProgress(_farthest);

    public bool HasStarted => _player is not null;

    private bool IsInCannon => _map.Player.Definition.Id == KnownObject.MannedCannon;

    private bool IsProtected
    {
        get
        {
            foreach (var link in _map.Player.EnumerateLinks())
            {
                if (link.Definition.Id is KnownObject.InvulnerabilityEffect or KnownObject.RescueMedkitEffect)
                    return true;
            }

            return false;
        }
    }

    private bool IsWakeTick => _map.Time % WakeInterval < _map.FrameTime;

    private bool HasDynamite => _state.HasDynamite;

    public void PlayTick()
    {
        if (!_map.HasPlayer)
        {
            if (HasStarted)
            {
                _input.Press(_command);
            }

            _map.StepAimed(_map.StepDemo(_map.WallTime, out _));

            return;
        }

        if (!HasStarted)
        {
            Start();
        }

        if (_map.Time % DecisionInterval < _map.FrameTime)
        {
            _command = PlanCommand();
        }

        var command = _command;
        var dodge = IsInCannon ? null : FindDodge(command.Destination) ?? (_seeksCannon ? null : SkirtCannon(command.Destination));

        if (dodge is not null)
        {
            command = command with { Destination = dodge };
        }

        var previous = _map.Player.Position.AsVector2();

        if (_input.Press(command))
        {
            _reachedGoal = null;
        }

        var player = _map.Player;
        Vector2? goal = player is { Command: Command.Move, HasGoal: true } ? player.GoalPosition.AsVector2() : null;
        _map.StepAimed(_map.StepDemo(_map.WallTime, out _));

        if (!_map.HasPlayer)
            return;

        if (goal is not null && player.Command != Command.Move)
        {
            _reachedGoal = goal;
        }

        ObserveMovement(previous);
    }

    private static float GetPickupReach(KnownObject id) => id.IsAmmo ? AmmoReach : PickupReach;

    private static bool IsWeapon(KnownObject id) => id is >= KnownObject.Shotgun and <= KnownObject.MagmaMinigun;

    private static int DiscountWeapon(int steps) => steps > 0 ? Math.Max(1, (int)(steps * DetourReach / FloodReach)) : steps;

    private static Vector2 GetCompassStep(int side)
    {
        float angle = side * MathF.Tau / CompassSides;

        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }

    private static bool ReachesAny(List<Sprite> bodies, Vector2 point, Vector2 half)
    {
        foreach (var body in bodies)
        {
            if (Reaches(body, point, half))
                return true;
        }

        return false;
    }

    private static bool Reaches(Sprite body, Vector2 point, Vector2 half)
    {
        var reach = body.Definition.Half + new Vector2(body.Definition.BlastRadius) + half;
        var offset = Vector2.Abs(body.Position.AsVector2() - point);

        return offset.X < reach.X && offset.Y < reach.Y;
    }

    private static bool IsParked(Sprite cannon) => cannon.Definition.Id == KnownObject.MountedCannon && cannon.Animation < SpriteAnimation.Death;

    private void SynchronizeCursor()
    {
        var player = _map.Player.Position.AsVector2();
        int best = _cursor;
        float bestDistance = _tour[_cursor].MeasureOctagonalDistance(player);
        float covered = 0;
        for (int i = _cursor + 1; i < _tour.Length && covered <= RejoinWindow; i++)
        {
            covered += Vector2.Distance(_tour[i], _tour[i - 1]);
            float distance = _tour[i].MeasureOctagonalDistance(player);

            if (distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }

        _cursor = best;
        _farthest = Math.Max(_farthest, _cursor);
    }

    private void SynchronizeMap()
    {
        bool respawned = _map.Player != _player;
        _player = _map.Player;

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
            _navigationTime = _map.Time;
            _progressTime = _map.Time;
        }

        bool terrainChanged = _field.Synchronize(_map);
        bool gatesChanged = _lastDoorsLocked != _map.AreDoorsLocked || _lastFencesUp != _map.AreFencesUp;
        bool changed = gatesChanged || terrainChanged;

        if (!changed)
            return;

        _unreachablePickups.Clear();
        _unreachableCrates.Clear();

        _lastDoorsLocked = _map.AreDoorsLocked;
        _lastFencesUp = _map.AreFencesUp;
        _field.SetDoorsLocked(_lastDoorsLocked);
        _path.Clear();
        _floodCell = -1;
        _offTour = true;

        if (gatesChanged)
        {
            SynchronizeCursor();
        }
    }

    private (Vector2 Position, int Cell, int Collected) GetPickupSpot(MergedMapSprite pickup)
    {
        if (!_pickupCells.TryGetValue(pickup, out var cached) ||
            PickupTracker.IsDropped(pickup) && Vector2.DistanceSquared(cached.Position, pickup.Position.AsVector2()) >= _field.CellStep * _field.CellStep)
        {
            var position = pickup.Position.AsVector2();
            int collected = -1;
            for (int i = 0; i < _tour.Length; i++)
            {
                if (_tour[i].MeasureOctagonalDistance(position) < GetPickupReach(pickup.Id))
                {
                    collected = i;
                }
            }

            cached = (position, _field.FindFree(position), collected);
            _pickupCells[pickup] = cached;
        }

        return cached;
    }

    private void Start()
    {
        _player = _map.Player;
        _aimCursor = PlayerCommand.GetCursor(_player.Position);
        _rubbleReach = (_map.Definitions.Find(KnownObject.RubblePile)?.Half ?? Vector2.Zero) + _player.Definition.Half;
    }

    private bool HasReached(Vector2 target) => (_reachedGoal.HasValue && _reachedGoal.Value == target) || PlayerMovement.IsTouching(_map.Player, target, _map.Definitions.Empty.Half);

    private PlayerCommand PlanCommand()
    {
        _playerGoal = null;
        SynchronizeMap();

        if (IsWakeTick || _aimTarget is not null && !_aimTarget.IsAlive)
        {
            FindTargets();
        }

        var target = _aimTarget ?? (_chained.Count == 0 ? FindBreach() ?? FindLootCrate() : null);
        var threat = _worstThreat is not null && _worstThreat.IsAlive ? _worstThreat : null;
        float worstIntrusion = threat is null ? float.MinValue : GetKeptDistance(threat.Definition) - _map.Player.Position.AsVector2().MeasureOctagonalDistance(threat.Position.AsVector2());

        if (target is not null && _map.Player.Link is { IsReloading: false })
        {
            _weapon = target == _aimTarget || target == _breachWall ? _gunnery.SelectWeapon(target) : Pistols;
        }

        int weapon = _weapon;

        Vector2? danger = null;
        foreach (var explosive in _map.ArmedDynamite)
        {
            if (_map.Player.Position.AsVector2().MeasureOctagonalDistance(explosive.Position.AsVector2()) < BlastSafetyReach)
            {
                danger = explosive.Position.AsVector2();
                break;
            }
        }

        if (danger is null)
        {
            _evadingBlast = false;
        }

        if (IsInCannon)
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
            if ((_path.Count == 0 || _map.Time > _pathEnds) && _map.Time >= _nextHuntAt)
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
        else if (_playerGoal is not null && _map.Player.Position.AsVector2().MeasureOctagonalDistance(_playerGoal.Value) >= 1)
        {
            _aimCursor = PlayerCommand.GetCursor(_map.Player.Position) + Vector2.Normalize(_playerGoal.Value - _map.Player.Position.AsVector2()) * IdleCursorReach;
        }

        return new PlayerCommand(_playerGoal, _aimCursor, target is not null, weapon);
    }

    private void ObserveMovement(Vector2 previous)
    {
        _blockedMovementTime = _playerGoal is not null && _map.Player.Position.AsVector2() == previous ? _blockedMovementTime + _map.FrameTime : 0;

        if (_playerGoal is null || Vector2.DistanceSquared(_map.Player.Position.AsVector2(), _navigationPosition) >= _map.Player.Definition.Object.Size.X * _map.Player.Definition.Object.Size.X)
        {
            _navigationPosition = _map.Player.Position.AsVector2();
            _navigationTime = _map.Time;
        }

        if (_playerGoal is null || Vector2.DistanceSquared(_map.Player.Position.AsVector2(), _progressPosition) >= _map.Player.Definition.Object.Size.X * _map.Player.Definition.Object.Size.X)
        {
            _progressPosition = _map.Player.Position.AsVector2();
            _progressTime = _map.Time;
        }

        if (_blockedMovementTime >= BlockedMovementTime || _map.Time - _navigationTime >= NavigationPatience)
        {
            bool stalled = _map.Time - _navigationTime >= NavigationPatience;

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
                _navigationPosition = _map.Player.Position.AsVector2();
                _navigationTime = _map.Time;
            }
        }
    }

    private void FindTargets()
    {
        FindChainedExplosives();
        _threats.Clear();
        Sprite? target = null;
        Sprite? threat = null;
        float targetPriority = float.MinValue;
        float worstIntrusion = float.MinValue;
        var reach = Vector2.Max(Gunnery.ScreenHalf, new Vector2(ThreatReach));
        int gunners = 0;
        foreach (var monster in _map.Collector.GetNear(_map.Player.Position.AsVector2() - reach, _map.Player.Position.AsVector2() + reach))
        {
            if (monster is { Army: Army.Monsters, IsAlive: true } && monster.Goal == _map.Player && monster.Definition.BattleRange > BackOffReach)
            {
                gunners++;
            }
        }

        _isChargingGunners = gunners <= MaxChargedGunners && _gunnery.HoldsShortReach();
        foreach (var monster in _map.Collector.GetNear(_map.Player.Position.AsVector2() - reach, _map.Player.Position.AsVector2() + reach))
        {
            if (!monster.IsAlive)
                continue;

            float distance = _map.Player.Position.AsVector2().MeasureOctagonalDistance(monster.Position.AsVector2());
            float intrusion = GetKeptDistance(monster.Definition) - distance;
            bool onScreen = _gunnery.IsOnScreen(monster.Position.AsVector2());
            float priority = MeasurePriority(monster, distance);
            bool isCloser = onScreen && priority > targetPriority;
            bool threatens = monster.Goal == _map.Player && intrusion > worstIntrusion - StandOffMargin && CanReach(monster);

            bool isGenerator = _map.AreFencesUp && monster.Definition.Id == KnownObject.Generator && monster.IsAlive;

            // Count off-screen attackers as threats only when they are within battle range and threaten the player
            if (monster == _map.Player || (monster.Army != Army.Monsters && !isGenerator) || (monster.Placement is not null && !isGenerator) ||
                !onScreen && (distance > monster.Definition.BattleRange + StandOffMargin || !threatens) || (!isCloser && !threatens) || !_gunnery.HasClearAim(monster))
                continue;

            if (isCloser)
            {
                target = monster;
                targetPriority = priority;
            }

            if (monster.Goal == _map.Player && intrusion > -StandOffMargin)
            {
                _threats.Add(monster);
            }

            if (monster.Goal == _map.Player && intrusion > worstIntrusion)
            {
                threat = monster;
                worstIntrusion = intrusion;
            }
        }

        _aimTarget = target;
        _worstThreat = threat;
    }

    private float MeasurePriority(Sprite monster, float distance)
    {
        var profile = _gunnery.Attacks.Get(monster.Definition);
        float danger = profile.Damage * 1000f / Math.Max(_map.FrameTime, monster.Definition.AttackTime);

        if (distance > monster.Definition.BattleRange + StandOffMargin)
        {
            danger *= DistantDangerShare;
        }

        return danger / Math.Max(1, monster.Hp) - distance * TieBreak;
    }

    private Sprite? FindLootCrate()
    {
        Sprite? best = null;
        float nearest = float.MaxValue;
        foreach (var sprite in _map.Collector.GetNear(_map.Player.Position.AsVector2() - Gunnery.ScreenHalf, _map.Player.Position.AsVector2() + Gunnery.ScreenHalf))
        {
            if (sprite.Definition.Id != KnownObject.Crate || !IsWantedContent(sprite) ||
                !_gunnery.IsOnScreen(sprite.Position.AsVector2()) || !_gunnery.HasClearAim(sprite))
                continue;

            float distance = Vector2.DistanceSquared(_map.Player.Position.AsVector2(), sprite.Position.AsVector2());

            if (distance < nearest)
            {
                nearest = distance;
                best = sprite;
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

            if (measure == CellScore.AwayFrom && IsBuriedBy(center, point))
                continue;

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

    private bool IsBuriedBy(Vector2 point, Vector2 explosive)
    {
        foreach (var hole in _holes)
        {
            var offset = Vector2.Abs(hole - explosive);
            var cover = Vector2.Abs(point - hole);

            if (offset is { X: <= RubbleBlastReach, Y: <= RubbleBlastReach } && cover.X <= _rubbleReach.X && cover.Y <= _rubbleReach.Y)
                return true;
        }

        return false;
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
        _nextHuntAt = _map.Time + HuntInterval;
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
        foreach (var sprite in _map.Monsters)
        {
            if (!sprite.IsAlive || sprite.Army != Army.Monsters)
                continue;

            int cell = _field.Find(sprite.Position.AsVector2());

            if (cell < 0 || _field.GetDistance(cell) < 0)
            {
                cell = _field.FindFree(sprite.Position.AsVector2(), reachedOnly: true);
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

        // Step toward the nearest standable cell outside the player's pocket when the player can reach too few cells to walk out of it
        if (_field.ReachedCount < MinPocketCells)
        {
            int nearest = _field.FindFree(_map.Player.Position.AsVector2(), StuckReach, skipReached: true, skippedHalf: _map.Player.Definition.Half + _map.Definitions.Empty.Half);

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

        int i = _cursor;
        while (i < _tourTargets.Length - 1 && _field.Find(_tourTargets[i]) < 0)
        {
            i++;
        }

        // Rejoin the route at the reachable tour point with the shortest walking distance among the next stretch of the route, leaving the remaining points to the hunt
        int bestIndex = -1;
        int bestGoal = -1;
        float covered = 0;
        for (int ahead = i; ahead < _tourTargets.Length && (bestIndex < 0 || covered <= RejoinWindow); ahead++)
        {
            covered += ahead > i ? Vector2.Distance(_tourTargets[ahead], _tourTargets[ahead - 1]) : 0;
            int goal = _field.FindFree(_tourTargets[ahead], reachedOnly: true);

            if (goal < 0 || HasReached(_field.GetCenter(goal)))
                continue;

            if (bestGoal < 0 || _field.GetDistance(goal) < _field.GetDistance(bestGoal))
            {
                bestIndex = ahead;
                bestGoal = goal;
            }
        }

        if (bestIndex >= 0)
        {
            _cursor = bestIndex;
            _farthest = Math.Max(_farthest, _cursor);

            return QueuePath(bestGoal, _tour[_cursor] = _field.GetCenter(bestGoal));
        }

        if (PlanBreach())
            return true;

        _cursor = _tour.Length - 1;

        return false;
    }

    private bool PlanBreach()
    {
        var player = _map.Player.Position.AsVector2();
        var ahead = _tourTargets[Math.Min(_cursor + 1, _tourTargets.Length - 1)];
        _breachWall = null;
        int goal = -1;
        float best = float.MaxValue;
        foreach (var body in _map.Bodies)
        {
            if (!body.IsAlive || body.Definition.Id is not (KnownObject.BreakableWall or KnownObject.BreakableWall2))
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

    private int FindFiringSpot(Sprite body)
    {
        var position = body.Position.AsVector2();
        CollectChain(position, body.Definition.Half, _cluster);
        for (int order = 0; order < _field.ReachedCount; order++)
        {
            int cell = _field.GetReached(order);
            var center = _field.GetCenter(cell);
            var offset = Vector2.Abs(center - position);

            if (offset.X >= Gunnery.ScreenHalf.X || offset.Y >= Gunnery.ScreenHalf.Y ||
                ReachesAny(_cluster, center, _map.Player.Definition.Half + new Vector2(BlastMargin)) ||
                !_gunnery.HasClearAim(body, center))
                continue;

            return cell;
        }

        return -1;
    }

    private bool IsNewWeapon(KnownObject id) =>
        id is >= KnownObject.Shotgun and <= KnownObject.MagmaMinigun && !_state.HoldsWeapon((int)id - Sprite.WeaponItemBase);

    private bool IsCached(MergedMapSprite pickup)
    {
        int slot = pickup.Id.GetAmmoSlot();
        foreach ((int weapon, var position) in _caches)
        {
            if (weapon == slot && position.MeasureOctagonalDistance(pickup.Position.AsVector2()) <= CacheReach)
                return true;
        }

        return false;
    }

    private bool IsWanted(KnownObject id)
    {
        int slot = id.GetAmmoSlot();

        return _state.CanTakePickup(id) && (slot < 0 || _state.HoldsWeapon(slot));
    }

    private bool IsWantedContent(Sprite holder)
    {
        foreach (int item in holder.Items)
        {
            if (IsWanted((KnownObject)item))
            {
                return true;
            }
        }

        return false;
    }

    private Sprite? FindBreach() =>
        _breachWall is not null && _breachWall.IsAlive && _gunnery.IsOnScreen(_breachWall.Position.AsVector2()) && _gunnery.HasClearAim(_breachWall)
            ? _breachWall : null;

    private bool PlanBreaker()
    {
        if (!_map.AreDoorsLocked)
        {
            return false;
        }

        foreach (var objective in _analysis.Objectives)
        {
            if (objective.Kind == ObjectiveKind.Breaker && QueueTrigger(new Vector2(objective.X, objective.Y), BreakerReach))
            {
                return true;
            }
        }

        return false;
    }

    private bool PlanGenerator()
    {
        if (!_map.AreFencesUp)
            return false;

        foreach (var body in _map.Bodies)
        {
            if (body is { IsAlive: true, Definition.Id: KnownObject.Generator } && QueueTrigger(body.Position.AsVector2(), BreakerReach))
                return true;
        }

        return false;
    }

    private bool PlanDynamite()
    {
        bool waits = false;
        foreach (var site in _map.DynamiteSites)
        {
            waits |= IsReachedOnTour(site.Position.AsVector2());
        }

        if (!waits)
            return false;

        if (HasDynamite)
        {
            foreach (var site in _map.DynamiteSites)
            {
                var point = site.Position.AsVector2();

                if (IsReachedOnTour(point) && QueueTrigger(point, BreakerReach))
                    return true;
            }

            return false;
        }

        foreach (var pickup in _pickups.Pickups)
        {
            int cell = pickup.Id == KnownObject.Dynamite ? GetPickupSpot(pickup).Cell : -1;

            if (cell >= 0 && _field.GetDistance(cell) >= 0 && QueuePath(cell, pickup.Position.AsVector2()))
                return true;
        }

        return false;
    }

    private bool PlantNearby()
    {
        if (_map.DynamiteSites.Count == 0)
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
            if (HasDynamite)
            {
                foreach (var site in _map.DynamiteSites)
                {
                    Consider(_field.FindTrigger(site.Position.AsVector2(), BreakerReach), site.Position.AsVector2());
                }
            }
            else
            {
                foreach (var pickup in _pickups.Pickups)
                {
                    if (pickup.Id == KnownObject.Dynamite)
                    {
                        Consider(GetPickupSpot(pickup).Cell, pickup.Position.AsVector2());
                    }
                }
            }

            _planting = best >= 0 && QueuePath(best, end, approachEnd: HasDynamite);

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
        var player = _map.Player.Position.AsVector2();
        if (HasDynamite)
        {
            foreach (var site in _map.DynamiteSites)
            {
                if (player.MeasureOctagonalDistance(site.Position.AsVector2()) < PlantSightReach)
                    return true;
            }

            return false;
        }

        foreach (var pickup in _pickups.Pickups)
        {
            if (pickup.Id == KnownObject.Dynamite && player.MeasureOctagonalDistance(pickup.Position.AsVector2()) < PlantSightReach)
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
        var offset = center - _map.Player.Position.AsVector2();
        float reach = _map.Player.Definition.Object.Size.X * 2;

        return offset.LengthSquared() <= reach * reach && _field.CanMove(_map.Player.Definition.Object, _map.Player.Position, center)
            ? Vector2.Dot(offset, _map.Player.Direction.Vector) : float.MinValue;
    }

    private bool StandsClear(Vector3 start, Vector2 step)
    {
        for (float distance = SideStepGrain; distance <= SideStepClearance; distance += SideStepGrain)
        {
            if (!_field.CanStand(_map.Player.Definition.Object, start + (step * distance).AsVector3()))
                return false;
        }

        return true;
    }

    private bool MovesClear(Vector3 start, Vector2 step, float length) =>
        _field.CanMove(_map.Player.Definition.Object, start, start.AsVector2() + step * length);

    private void FindChainedExplosives() =>
        CollectChain(_map.Player.Position.AsVector2(), _map.Player.Definition.Half + new Vector2(BlastMargin), _chained);

    private void CollectChain(Vector2 point, Vector2 half, List<Sprite> chain)
    {
        _explosives.Clear();
        chain.Clear();
        foreach (var body in _map.Collector.GetNear(point - Gunnery.ScreenHalf, point + Gunnery.ScreenHalf))
        {
            if (body.Placement is not null && body is { IsAlive: true, Definition.Damage: > 0 })
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

        for (int i = 0; i < chain.Count; i++)
        {
            foreach (var body in _explosives)
            {
                if (!chain.Contains(body) && Reaches(body, chain[i].Position.AsVector2(), chain[i].Definition.Half))
                {
                    chain.Add(body);
                }
            }
        }
    }

    private bool CanReach(Sprite monster) =>
        monster.Definition.BattleRange > BackOffReach ? !_field.CrossesClosedGate(monster.Position.AsVector2(), _map.Player.Position.AsVector2()) :
            _field.CanMove(monster.Position, _map.Player.Position.AsVector2());

    private float GetKeptDistance(SpriteDefinition definition)
    {
        if (IsProtected)
            return 0;

        if (definition.BattleRange <= BackOffReach)
            return MathF.Max(BackOffReach, definition.Half.Length() * 2);

        if (_isChargingGunners && definition.Id is not (KnownObject.TankWeaponMonster or KnownObject.YellowTankWeaponMonster or KnownObject.RedTankWeaponMonster))
            return BackOffReach;

        return MathF.Max(BackOffReach, (definition.BattleRange + StandOffMargin) * GunnerKeep);
    }

    private void PlanEscape()
    {
        _path.Clear();
        int start = FloodPlayer(_threats.Count >= CrowdSize ? CrowdEscapeSteps : EscapeSteps);
        _floodCell = -1;
        int best = FindEscapePickup();
        best = best >= 0 ? best : FindBestReached(CellScore.Escape, float.MinValue);

        if (best != start)
        {
            QueuePath(best, _field.GetCenter(best));
            _pathEnds = int.MaxValue;
        }
    }

    private int FindEscapePickup()
    {
        int best = -1;
        float bestScore = 0;
        foreach (var pickup in _pickups.Pickups)
        {
            if (!IsWanted(pickup.Id) || _unreachablePickups.Contains(pickup))
                continue;

            int cell = _field.Find(pickup.Position.AsVector2());

            if (cell < 0 || _field.GetDistance(cell) <= 0)
                continue;

            float score = MeasureEscape(_field.GetCenter(cell));

            if (score > bestScore)
            {
                best = cell;
                bestScore = score;
            }
        }

        return best;
    }

    private float MeasureEscape(Vector2 center)
    {
        float score = float.MaxValue;
        float offLine = float.MaxValue;
        bool covered = true;
        foreach (var threat in _threats)
        {
            float distance = center.MeasureOctagonalDistance(threat.Position.AsVector2());
            score = MathF.Min(score, distance - GetKeptDistance(threat.Definition));

            if (threat.Definition.BattleRange <= BackOffReach)
                continue;

            offLine = MathF.Min(offLine, MeasureOffLine(threat.Position.AsVector2(), center));
            covered = covered && (distance > threat.Definition.BattleRange || !_gunnery.HasClearAim(threat, center));
        }

        if (offLine < float.MaxValue)
        {
            score += DodgeWeight * offLine + (covered ? CoverBonus : 0);
        }

        return ReachesAny(_explosives, center, _map.Player.Definition.Half) ? score - ExplosivePenalty : score;
    }

    private Vector2? FindDodge(Vector2? goal)
    {
        _incoming.Clear();
        var center = _map.Player.Position.AsVector2();
        foreach (var round in _map.ActiveSprites)
        {
            if (round.Shooter is null || round.Shooter.Army == _map.Player.Army || round.Speed <= 0 || round.Speed >= SpriteDefinition.Unlimited ||
                round.Definition.Damage <= 0 || center.MeasureOctagonalDistance(round.Position.AsVector2()) > DodgeReach)
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

            if (!StandsClear(_map.Player.Position, step))
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

    private float MeasureFirstHit(Vector2 vector)
    {
        var start = _map.Player.Position.AsVector2();
        float speed = _state.Speed / 1000f;
        float first = DodgeHorizon;
        foreach (var round in _incoming)
        {
            var velocity = round.Direction.Vector * round.Speed;
            var reach = _map.Player.Definition.Half + round.Definition.Half + new Vector2(round.Definition.BlastRadius);
            for (float time = 0; time < first; time += DodgeStep)
            {
                var offset = Vector2.Abs(start + vector * speed * time - (round.Position.AsVector2() + velocity * time));

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
        if (_aimTarget is null || _aimTarget.Army != Army.Monsters || _map.Time - _cannonIdleSince < CannonRest || CountMonstersOnScreen() < CrowdSize)
        {
            _seeksCannon = false;

            return false;
        }

        if (_seeksCannon && _path.Count > 0)
            return true;

        var center = _map.Player.Position.AsVector2();
        foreach (var cannon in _map.ScriptedSprites)
        {
            var position = cannon.Position.AsVector2();

            if (!IsParked(cannon) || center.MeasureOctagonalDistance(position) > CannonSeekReach)
                continue;

            FloodWithin(CannonSeekReach * 2);

            if (QueueTrigger(position, CannonApproach))
                return _seeksCannon = true;
        }

        return _seeksCannon = false;
    }

    private void ManCannon()
    {
        if (_aimTarget is not null && _aimTarget.Army == Army.Monsters && CountMonstersOnScreen() >= CrowdSize)
        {
            _cannonIdleSince = _map.Time;
        }
        else if (_map.Time - _cannonIdleSince > CannonPatience)
        {
            _playerGoal = _tour[Math.Min(_cursor + 1, _tour.Length - 1)];
        }
    }

    private int CountMonstersOnScreen()
    {
        int count = 0;
        var center = _map.Player.Position.AsVector2();
        foreach (var sprite in _map.Collector.GetNear(center - Gunnery.ScreenHalf, center + Gunnery.ScreenHalf))
        {
            if (sprite is { IsAlive: true, Army: Army.Monsters, Placement: null })
            {
                count++;
            }
        }

        return count;
    }

    // Treats a tour point inside a parked cannon's reach as passed once the player has come close to that cannon
    private bool IsBesideCannon(Vector2 point)
    {
        foreach (var cannon in _map.ScriptedSprites)
        {
            var position = cannon.Position.AsVector2();

            if (IsParked(cannon) && point.MeasureOctagonalDistance(position) < CannonKeep &&
                _map.Player.Position.AsVector2().MeasureOctagonalDistance(position) < CannonKeep + CannonStep * 2)
                return true;
        }

        return false;
    }

    private Vector2? SkirtCannon(Vector2? goal)
    {
        if (goal is null)
            return null;

        var center = _map.Player.Position.AsVector2();
        var heading = goal.Value - center;

        if (heading.LengthSquared() < 1)
            return null;

        heading = Vector2.Normalize(heading);
        foreach (var cannon in _map.ScriptedSprites)
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
        var line = _map.Player.Position.AsVector2() - gunner;

        if (line.LengthSquared() < 1)
            return 0;

        var side = spot - _map.Player.Position.AsVector2();

        return MathF.Abs(line.X * side.Y - line.Y * side.X) / line.Length();
    }

    private bool PlanSecret(int maxSteps)
    {
        if (_map.Time < _nextSecretAt)
            return false;

        Sprite? wall = null;
        int spot = -1;
        int nearest = maxSteps;
        foreach (var element in _analysis.Elements)
        {
            if (element.Kind != ElementKind.SecretWall)
                continue;

            var point = new Vector2(element.X, element.Y);
            Sprite? body = null;
            foreach (var candidate in _map.Collector.GetNear(point - new Vector2(SideStep), point + new Vector2(SideStep)))
            {
                if (candidate.Placement is not null && candidate is { IsAlive: true, Definition.Id: KnownObject.BreakableWall })
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
            _nextSecretAt = _map.Time + SecretInterval;

            return false;
        }

        if (_breachWall != wall)
        {
            _breachWall = wall;
            _breachSince = _map.Time;
        }

        return StartDetour(spot, _field.GetCenter(spot));

        bool HidesNewWeapon(Vector2 point)
        {
            foreach (var pickup in _pickups.Pickups)
            {
                if (Vector2.Distance(pickup.Position.AsVector2(), point) < SecretReach && IsNewWeapon(pickup.Id))
                    return true;
            }

            return false;
        }

        bool HidesLoot(Vector2 point)
        {
            foreach (var pickup in _pickups.Pickups)
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
        var pickups = _pickups.Pickups;
        for (int i = 0; i < pickups.Count; i++)
        {
            var pickup = pickups[i];

            if (_unreachablePickups.Contains(pickup))
                continue;

            (_, int cell, int collected) = GetPickupSpot(pickup);
            bool cached = IsCached(pickup);
            bool counts = cell >= 0 && (collected <= _cursor || PickupTracker.IsDropped(pickup) || IsNewWeapon(pickup.Id) || cached);

            if (counts && _field.GetDistance(cell) < 0)
            {
                cell = _field.FindFree(pickup.Position.AsVector2(), GetPickupReach(pickup.Id) * 2, reachedOnly: true);
            }

            int steps = counts && cell >= 0 ? _field.GetDistance(cell) : -1;

            if (IsNewWeapon(pickup.Id) || cached)
            {
                steps = DiscountWeapon(steps);
            }

            if (steps > 0 && steps <= bestSteps && _field.GetCenter(cell).MeasureOctagonalDistance(pickup.Position.AsVector2()) < GetPickupReach(pickup.Id) * 2 &&
                IsWanted(pickup.Id))
            {
                best = i;
                bestCell = cell;
                bestSteps = steps;
            }
        }

        Sprite? crate = null;
        int crateCell = -1;
        foreach (var body in _map.Bodies)
        {
            if (body.Definition.Id != KnownObject.Crate || !body.IsAlive || _unreachableCrates.Contains(body) ||
                !IsWantedContent(body))
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

    private bool IsBreaching(Sprite? target) => target is not null && target == _breachWall && _map.Time - _breachSince <= BreachPatience;

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

        foreach (var pickup in _pickups.Pickups)
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
        else if (_detourTarget is not null && IsWeapon(_detourTarget.Id))
        {
            _caches.Add(((int)_detourTarget.Id - Sprite.WeaponItemBase, _detourTarget.Position.AsVector2()));
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
        for (int i = _way.Count - 1; i >= 0; i--)
        {
            _path.Enqueue(_way[i]);
        }

        var last = _field.GetCenter(goal);
        _path.Enqueue(approachEnd || _field.CanMove(new Vector3(last, _map.Movement.GetSupportHeight(_map.Player.Definition.Object, last)), end) ? end : last);

        _pathEnds = _map.Time + (int)(_path.Count * _field.CellStep * 1000 / _state.Speed) + DetourSlack;

        return true;
    }

    private void FollowPath()
    {
        if (_map.Time > _pathEnds)
        {
            _path.Clear();
        }
        while (_path.Count > 0 && HasReached(_path.Peek()))
        {
            _path.Dequeue();
        }

        if (_path.Count > 1 && IsWakeTick)
        {
            var start = _map.Player.Position.AsVector2();
            var previous = start;
            int index = 0;
            int skip = 0;
            foreach (var point in _path)
            {
                if (index >= PathLookahead || Vector2.Dot(point - previous, previous - start) < 0 ||
                    !_field.CanMove(new Vector3(start, _map.Player.Position.Z), point))
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
            var offset = point - _map.Player.Position.AsVector2();
            _playerGoal = SteerClear(_path.Count > 1 ? point + Vector2.Normalize(offset) * SteeringReach : point);
        }
    }

    // The engine's obstacle avoidance swings the direction in place when the first steps toward a goal are blocked at a thin corner
    private Vector2 SteerClear(Vector2 goal)
    {
        var start = _map.Player.Position;
        var toward = goal - start.AsVector2();

        if (toward.LengthSquared() < 1 || _worstThreat is not null || _map.Time - _progressTime < SteerPatience)
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

            if (!HasReached(_tour[next]) && !IsBesideCannon(_tour[next]))
            {
                if (!_field.CanMove(_map.Player.Position, _tour[next]))
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
        int cell = _field.FindStart(_map.Player.Position);
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
}
