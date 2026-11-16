namespace ASToolkit.Core.Engine;

internal readonly record struct UpdateEntry(UpdateRole Role, Unit? Unit);

internal sealed class UpdateOrder
{
    public const int LayerCount = 17;
    public const int UpdatedLayerCount = 16;
    private const int CompactionPeriodMask = 3;

    private readonly List<UpdateEntry>[] _layers = new List<UpdateEntry>[LayerCount];

    public UpdateOrder()
    {
        for (int layer = 0; layer < LayerCount; layer++)
        {
            _layers[layer] = [];
        }
    }

    public List<UpdateEntry> GetLayer(int layer) => _layers[layer];

    [EngineRoutine(0x0040fad0)]
    public void Add(int layer, UpdateEntry entry) => _layers[layer].Add(entry);

    [EngineRoutine(0x0040fa90)]
    public void Remove(int layer, Unit unit)
    {
        var list = _layers[layer];
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].Unit != unit)
                continue;

            Clear(list, i);

            return;
        }
    }

    public static void Clear(List<UpdateEntry> list, int index) => list[index] = default;

    [EngineRoutine(0x0040f210)]
    public void Compact(int time)
    {
        if ((time & CompactionPeriodMask) != 0)
            return;

        foreach (var list in _layers)
        {
            list.RemoveAll(entry => entry.Unit is null);
        }
    }
}
