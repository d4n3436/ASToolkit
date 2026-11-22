using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Bot;

internal sealed class PlayLog
{
    private readonly HashSet<Sprite> _dead = new(ReferenceEqualityComparer.Instance);
    private readonly PickupTracker _pickups;
    private readonly List<KnownObject> _taken = [];
    private bool _doorsLocked;
    private bool _fencesUp;
    private Sprite? _player;

    public PlayLog(Map map)
    {
        _doorsLocked = map.AreDoorsLocked;
        _fencesUp = map.AreFencesUp;
        Timeline = new ScriptTimeline(map.AreDoorsLocked, null, null, null);
        MinHealth = int.MaxValue;
        _pickups = new PickupTracker(map);
    }

    public int Kills { get; private set; }

    public int Respawns { get; private set; }

    public int MinHealth { get; private set; }

    public ScriptTimeline Timeline { get; private set; }

    public IReadOnlyList<KnownObject> TakenPickups => _taken;

    public void Update(Map map)
    {
        if (map.HasPlayer && map.Player != _player)
        {
            if (_player is { IsAlive: false })
            {
                Respawns++;
                MinHealth = Math.Min(MinHealth, 0);
            }

            _player = map.Player;
        }

        if (map.HasPlayer)
        {
            MinHealth = Math.Min(MinHealth, map.Player.Hp);
        }

        UpdateTimeline(map);
        UpdatePickups();
        foreach (var sprite in map.Monsters)
        {
            if (sprite.Placement is null && sprite.DiedAt >= 0 && _dead.Add(sprite))
            {
                Kills++;
            }
        }
    }

    private void UpdateTimeline(Map map)
    {
        if (_doorsLocked != map.AreDoorsLocked)
        {
            _doorsLocked = map.AreDoorsLocked;
            Timeline = _doorsLocked
                ? Timeline with { BlackoutAt = Timeline.BlackoutAt ?? map.Time }
                : Timeline with { DoorsOpenedAt = Timeline.DoorsOpenedAt ?? map.Time };
        }

        if (_fencesUp && !map.AreFencesUp)
        {
            _fencesUp = false;
            Timeline = Timeline with { FencesDownAt = map.Time };
        }
    }

    private void UpdatePickups()
    {
        foreach (var pickup in _pickups.Removed)
        {
            if (!_pickups.WasTaken(pickup))
                continue;

            _taken.Add(pickup.Id);
        }
    }
}
