using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Simulation;

internal sealed class PlayLog
{
    private readonly HashSet<Unit> _dead = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<WorldPickup> _pickups = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<WorldPickup> _present = new(ReferenceEqualityComparer.Instance);
    private readonly List<KnownObject> _taken = [];
    private readonly List<SpriteEvent> _pickupEvents = [];
    private bool _doorsLocked;
    private bool _fencesUp;
    private int _lives;

    public PlayLog(GameWorld world)
    {
        _doorsLocked = world.DoorsLocked;
        _fencesUp = world.FencesUp;
        _lives = GetLives(world);
        Timeline = new ScriptTimeline(world.DoorsLocked, null, null, null);
        MinHealth = world.Player.Hp;
        foreach (var pickup in world.Pickups)
        {
            _pickups.Add(pickup);
        }
    }

    public int Kills { get; private set; }

    public int Respawns { get; private set; }

    public int MinHealth { get; private set; }

    public ScriptTimeline Timeline { get; private set; }

    public IReadOnlyList<KnownObject> TakenPickups => _taken;

    public IReadOnlyList<SpriteEvent> PickupEvents => _pickupEvents;

    public void Update(GameWorld world)
    {
        int lives = GetLives(world);

        if (lives < _lives)
        {
            Respawns += _lives - lives;
            MinHealth = Math.Min(MinHealth, 0);
        }

        _lives = lives;
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
        if (_doorsLocked != world.DoorsLocked)
        {
            _doorsLocked = world.DoorsLocked;
            Timeline = _doorsLocked
                ? Timeline with { BlackoutAt = Timeline.BlackoutAt ?? world.Time }
                : Timeline with { DoorsOpenedAt = Timeline.DoorsOpenedAt ?? world.Time };
        }

        if (_fencesUp && !world.FencesUp)
        {
            _fencesUp = false;
            Timeline = Timeline with { FencesDownAt = world.Time };
        }
    }

    // Counts a pickup gone before its time ran out as taken
    private void UpdatePickups(GameWorld world)
    {
        _present.Clear();
        foreach (var pickup in world.Pickups)
        {
            _present.Add(pickup);
        }

        foreach (var pickup in _pickups)
        {
            if (_present.Contains(pickup) || world.Time >= pickup.ExpiresAt)
                continue;

            _taken.Add(pickup.Id);

            if (pickup.Placement is not null)
            {
                _pickupEvents.Add(new SpriteEvent(pickup.Placement, world.Time));
            }
        }

        _pickups.Clear();
        _pickups.UnionWith(_present);
    }

    private static int GetLives(GameWorld world) => world.Inventory.Items.GetValueOrDefault(KnownObject.ExtraLife);
}
