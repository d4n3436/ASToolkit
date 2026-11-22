using ASToolkit.Engine;
using ASToolkit.Formats;
using ASToolkit.Analysis.Levels;

namespace ASToolkit.Bot;

internal sealed class PickupTracker
{
    private readonly Map _map;
    private readonly List<MergedMapSprite> _pickups = [];
    private readonly List<MergedMapSprite> _removed = [];
    private readonly Dictionary<MergedMapSprite, Sprite?> _sprites = new(ReferenceEqualityComparer.Instance);
    private int _scanned;
    private int _checkedAt = -1;

    public PickupTracker(Map map)
    {
        _map = map;
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
        var sprite = _sprites.GetValueOrDefault(pickup);

        return sprite is not null && sprite.Placement == pickup && sprite.Animation == SpriteAnimation.Death2;
    }

    private static bool IsPickup(KnownObject id) => id.IsPickup && id is not (>= KnownObject.WornGreenArmor and <= KnownObject.InvulnerabilityEffect);

    private void Update()
    {
        if (_checkedAt == _map.Time)
            return;

        _checkedAt = _map.Time;
        _removed.Clear();
        var sprites = _map.Sprites;
        for (; _scanned < sprites.Count; _scanned++)
        {
            if (IsPickup(sprites[_scanned].Id))
            {
                _pickups.Add(sprites[_scanned]);
            }
        }

        for (int i = _pickups.Count - 1; i >= 0; i--)
        {
            var pickup = _pickups[i];

            if (_map.IsRemoved(pickup))
            {
                _removed.Add(pickup);
                _pickups.RemoveAt(i);
                continue;
            }

            _sprites[pickup] = _map.FindPlacementSprite(pickup);
        }
    }
}
