using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Simulation;

/// <summary>
/// Tracks the effects that were baked into the ground when they ended, by comparing the update lists of a map between frames.
/// </summary>
public sealed class MapObserver
{
    private readonly Map _map;
    private readonly List<Sprite>[] _previous = new List<Sprite>[UpdateOrder.LayerCount];
    private readonly List<EffectState> _baked = [];
    private readonly HashSet<Sprite> _alive = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="MapObserver"/> class.
    /// </summary>
    /// <param name="map">The map to observe.</param>
    public MapObserver(Map map)
    {
        _map = map;
        for (int layer = 0; layer < _previous.Length; layer++)
        {
            _previous[layer] = [];
        }

        Observe();
    }

    /// <summary>
    /// Gets the effects that were baked into the ground when they ended, in the order the update loop removed them.
    /// </summary>
    public IReadOnlyList<EffectState> BakedEffects => _baked;

    /// <summary>
    /// Compares the update lists with those of the previous call. Call it after every frame.
    /// </summary>
    public void Observe()
    {
        for (int layer = 0; layer < _previous.Length; layer++)
        {
            var current = _map.GetLayer(layer);
            var previous = _previous[layer];
            _alive.Clear();
            for (int i = 0; i < current.Count; i++)
            {
                var sprite = current[i];

                if (sprite is not null)
                {
                    _alive.Add(sprite);
                }
            }

            for (int i = previous.Count - 1; i >= 0; i--)
            {
                var sprite = previous[i];

                if (!_alive.Contains(sprite) && sprite.Animation == SpriteAnimation.Death && sprite.Placement is null && Map.IsBaked(sprite.Definition))
                {
                    _baked.Add(SpawnedObjectSystem.GetState(sprite));
                }
            }

            previous.Clear();
            for (int i = 0; i < current.Count; i++)
            {
                var sprite = current[i];

                if (sprite is not null)
                {
                    previous.Add(sprite);
                }
            }
        }
    }
}
