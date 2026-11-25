using ASToolkit.Analysis.Levels;
using ASToolkit.Engine;
using ASToolkit.Formats;

namespace ASToolkit.Bot;

internal sealed class PickupTracker
{
    private readonly Map _map;
    private readonly List<Sprite> _pickups = [];
    private readonly List<Sprite> _removed = [];
    private readonly HashSet<Sprite> _seen = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Sprite> _placed = new(ReferenceEqualityComparer.Instance);
    private int _population;
    private int _checkedAt = -1;

    public PickupTracker(Map map)
    {
        _map = map;
        _population = GetPopulation();
        Scan();
        _placed.UnionWith(_pickups);
    }

    public IReadOnlyList<Sprite> Pickups
    {
        get
        {
            Update();
            return _pickups;
        }
    }

    public IReadOnlyList<Sprite> Removed
    {
        get
        {
            Update();
            return _removed;
        }
    }

    public static bool WasTaken(Sprite pickup) => pickup.Animation == SpriteAnimation.Death2;

    public bool IsDropped(Sprite pickup) => !_placed.Contains(pickup);

    private static bool IsPickup(KnownObject id) => id.IsPickup && id is not (>= KnownObject.WornGreenArmor and <= KnownObject.InvulnerabilityEffect);

    private int GetPopulation()
    {
        int total = 0;
        for (int layer = 0; layer < Map.LayerCount; layer++)
        {
            total += _map.GetLayer(layer).Count;
        }

        return total;
    }

    private void Scan()
    {
        foreach (var sprite in _map.UpdatedSprites)
        {
            if (IsPickup(sprite.Vid.Id) && _seen.Add(sprite))
            {
                _pickups.Add(sprite);
            }
        }
    }

    private void Update()
    {
        if (_checkedAt == _map.CurrentTime)
            return;

        _checkedAt = _map.CurrentTime;
        _removed.Clear();
        int population = GetPopulation();

        if (population != _population)
        {
            _population = population;
            Scan();
        }

        for (int i = _pickups.Count - 1; i >= 0; i--)
        {
            var pickup = _pickups[i];

            if (pickup.Animation >= SpriteAnimation.Death || pickup.ReferenceCount <= 0)
            {
                _removed.Add(pickup);
                _pickups.RemoveAt(i);
            }
        }
    }
}
