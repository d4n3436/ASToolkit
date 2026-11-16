using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Simulation;

internal sealed class PickupTracker
{
    private readonly GameWorld _world;
    private readonly List<MergedMapSprite> _pickups = [];
    private readonly List<MergedMapSprite> _removed = [];
    private readonly Dictionary<MergedMapSprite, Unit?> _units = new(ReferenceEqualityComparer.Instance);
    private int _scanned;
    private int _checkedAt = -1;

    public PickupTracker(GameWorld world)
    {
        _world = world;
    }

    public IReadOnlyList<MergedMapSprite> Pickups
    {
        get
        {
            Update();
            return _pickups;
        }
    }

    public IReadOnlyList<MergedMapSprite> Removed
    {
        get
        {
            Update();
            return _removed;
        }
    }

    public static bool IsDropped(MergedMapSprite pickup) => pickup.Handle == 0;

    public bool WasTaken(MergedMapSprite pickup)
    {
        var unit = _units.GetValueOrDefault(pickup);

        return unit is not null && unit.Placement == pickup && unit.Animation == SpriteAnimation.Death2;
    }

    private static bool IsPickup(KnownObject id) => id.IsPickup && id is not (>= KnownObject.WornGreenArmor and <= KnownObject.InvulnerabilityEffect);

    private void Update()
    {
        if (_checkedAt == _world.Time)
            return;

        _checkedAt = _world.Time;
        _removed.Clear();
        var sprites = _world.Sprites;
        for (; _scanned < sprites.Count; _scanned++)
        {
            if (IsPickup(sprites[_scanned].Id))
            {
                _pickups.Add(sprites[_scanned]);
            }
        }

        for (int index = _pickups.Count - 1; index >= 0; index--)
        {
            var pickup = _pickups[index];

            if (_world.IsRemoved(pickup))
            {
                _removed.Add(pickup);
                _pickups.RemoveAt(index);
                continue;
            }

            _units[pickup] = _world.FindPlacementUnit(pickup);
        }
    }
}
