using ASToolkit.Engine;
using ASToolkit.Formats;

namespace ASToolkit.Tests.Engine;

public sealed class MapObserver
{
    private readonly Map _map;
    private readonly List<Sprite>[] _previous = new List<Sprite>[Map.LayerCount];
    private readonly List<EffectState> _baked = [];
    private readonly HashSet<Sprite> _alive = [];

    public MapObserver(Map map)
    {
        _map = map;
        for (int layer = 0; layer < _previous.Length; layer++)
        {
            _previous[layer] = [];
        }

        Observe();
    }

    public IReadOnlyList<EffectState> BakedEffects => _baked;

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

                if (!_alive.Contains(sprite) && sprite.Animation == SpriteAnimation.Death && Map.IsBaked(sprite.Definition))
                {
                    _baked.Add(GetState(sprite));
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

    private static EffectState GetState(Sprite sprite) =>
        new(sprite.Definition.Id, sprite.Position, sprite.Direction, sprite.Animation, sprite.CreatedAt, sprite.Frame, sprite.KeyframePosition);
}
