using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Simulation;

internal sealed class EnemyStock
{
    private readonly GameWorld _world;
    private readonly Dictionary<Unit, (int Key, int Count)> _unbuilt = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Unit> _seen = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Unit> _dead = new(ReferenceEqualityComparer.Instance);
    private int _alive;

    public EnemyStock(GameWorld world)
    {
        _world = world;
        Update();
    }

    public void Update()
    {
        foreach (var unit in _world.Monsters)
        {
            if (unit.Placement is null && _seen.Add(unit) && IsCounted(unit))
            {
                _alive++;
            }

            if (!unit.IsAlive && _dead.Add(unit) && IsCounted(unit))
            {
                _alive--;
            }
        }
    }

    public int Count
    {
        get
        {
            int count = _alive;
            foreach (var builder in _world.Builders)
            {
                count += CountUnbuilt(builder);
            }

            return count;
        }
    }

    private static bool IsCountedObject(KnownObject id) => KnownObject.Monsters.Contains(id) || KnownObject.Bosses.Contains(id);

    private static bool IsCounted(Unit unit) => unit.Army == Army.Monsters && IsCountedObject(unit.Definition.Id);

    private int CountUnbuilt(Unit builder)
    {
        var pending = _world.GetPendingBuild(builder);
        int key = builder.Orders.Count * 2 + (pending is null ? 0 : 1);

        if (_unbuilt.TryGetValue(builder, out var cached) && cached.Key == key)
            return cached.Count;

        int count = (pending is not null && IsCountedObject(pending.Id) ? 1 : 0) + MeasureUnbuilt(builder.Orders.Orders);
        _unbuilt[builder] = (key, count);

        return count;
    }

    private static int MeasureUnbuilt(ReadOnlySpan<UnitOrder> orders)
    {
        int count = 0;
        for (int index = orders.Length - 1; index >= 0; index--)
        {
            var order = orders[index];

            switch (order.Code)
            {
                case ActionCode.BuildUnit when IsCountedObject((KnownObject)order.Var1):
                    count++;
                    break;
                case ActionCode.GotoStack or ActionCode.StopStack or ActionCode.ClearStack:
                    return count;
            }
        }

        return count;
    }
}
