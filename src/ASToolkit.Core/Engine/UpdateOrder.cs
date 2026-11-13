namespace ASToolkit.Core.Engine;

internal enum UpdateRole
{
    None,
    Player,
    Torso,
    Monster,
    Scripted,
    Effect,
    Program
}

internal readonly record struct UpdateEntry(UpdateRole Role, Unit? Unit, ObjectProgram? Program);

internal sealed class UpdateOrder
{
    public const int LayerCount = 16;

    private readonly List<UpdateEntry>[] _layers = new List<UpdateEntry>[LayerCount];
    private int _holes;

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
        for (int index = list.Count - 1; index >= 0; index--)
        {
            if (list[index].Unit != unit)
                continue;

            Clear(list, index);

            return;
        }
    }

    public void Clear(List<UpdateEntry> list, int index)
    {
        list[index] = default;
        _holes++;
    }

    // Drops emptied entries once they outnumber the live ones, keeping the order
    public void Compact()
    {
        int live = _layers.Sum(list => list.Count) - _holes;

        if (_holes <= live)
            return;

        foreach (var list in _layers)
        {
            list.RemoveAll(entry => entry.Role == UpdateRole.None);
        }

        _holes = 0;
    }
}
