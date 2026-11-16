using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Simulation;

internal sealed class PlayLog
{
    private readonly HashSet<Unit> _dead = new(ReferenceEqualityComparer.Instance);
    private readonly PickupTracker _pickups;
    private readonly List<KnownObject> _taken = [];
    private readonly List<SpriteEvent> _pickupEvents = [];
    private bool _doorsLocked;
    private bool _fencesUp;
    private Unit _player;

    public PlayLog(GameWorld world)
    {
        _doorsLocked = world.AreDoorsLocked;
        _fencesUp = world.AreFencesUp;
        _player = world.Player;
        Timeline = new ScriptTimeline(world.AreDoorsLocked, null, null, null);
        MinHealth = world.Player.Hp;
        _pickups = new PickupTracker(world);
    }

    public int Kills { get; private set; }

    public int Respawns { get; private set; }

    public int MinHealth { get; private set; }

    public ScriptTimeline Timeline { get; private set; }

    public IReadOnlyList<KnownObject> TakenPickups => _taken;

    public IReadOnlyList<SpriteEvent> PickupEvents => _pickupEvents;

    public void Update(GameWorld world)
    {
        if (world.Player != _player)
        {
            if (!_player.IsAlive)
            {
                Respawns++;
                MinHealth = Math.Min(MinHealth, 0);
            }

            _player = world.Player;
        }

        MinHealth = Math.Min(MinHealth, world.Player.Hp);
        UpdateTimeline(world);
        UpdatePickups(world);
        foreach (var unit in world.Monsters)
        {
            if (unit.Placement is null && unit.DiedAt >= 0 && _dead.Add(unit))
            {
                Kills++;
            }
        }
    }

    private void UpdateTimeline(GameWorld world)
    {
        if (_doorsLocked != world.AreDoorsLocked)
        {
            _doorsLocked = world.AreDoorsLocked;
            Timeline = _doorsLocked
                ? Timeline with { BlackoutAt = Timeline.BlackoutAt ?? world.Time }
                : Timeline with { DoorsOpenedAt = Timeline.DoorsOpenedAt ?? world.Time };
        }

        if (_fencesUp && !world.AreFencesUp)
        {
            _fencesUp = false;
            Timeline = Timeline with { FencesDownAt = world.Time };
        }
    }

    private void UpdatePickups(GameWorld world)
    {
        foreach (var pickup in _pickups.Removed)
        {
            if (!_pickups.WasTaken(pickup))
                continue;

            _taken.Add(pickup.Id);

            if (!PickupTracker.IsDropped(pickup))
            {
                _pickupEvents.Add(new SpriteEvent(pickup, world.Time));
            }
        }
    }
}
