namespace ASToolkit.Core.Engine;

internal sealed class UpdateOrder
{
    public const int LayerCount = 17;
    public const int UpdatedLayerCount = 16;
    private const int CompactionPeriodMask = 3;

    private readonly List<Sprite?>[] _layers = new List<Sprite?>[LayerCount];

    public UpdateOrder()
    {
        for (int layer = 0; layer < LayerCount; layer++)
        {
            _layers[layer] = [];
        }
    }

    public static void Clear(List<Sprite?> list, int index) => list[index] = null;

    public List<Sprite?> GetLayer(int layer) => _layers[layer];

    public void Add(int layer, Sprite sprite) => _layers[layer].Add(sprite);

    public bool HasSprites(int layer)
    {
        foreach (var sprite in _layers[layer])
        {
            if (sprite is not null)
                return true;
        }

        return false;
    }

    [EngineRoutine(0x0040fa90)]
    public bool Remove(int layer, Sprite sprite)
    {
        var list = _layers[layer];
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i] != sprite)
                continue;

            Clear(list, i);

            return true;
        }

        return false;
    }

    public void Compact(int time)
    {
        if ((time & CompactionPeriodMask) != 0)
            return;

        foreach (var list in _layers)
        {
            int kept = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is not null)
                {
                    list[kept++] = list[i];
                }
            }

            list.RemoveRange(kept, list.Count - kept);
        }
    }
}
