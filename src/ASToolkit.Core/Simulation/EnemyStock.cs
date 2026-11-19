using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Simulation;

internal sealed class EnemyStock
{
    private readonly Map _map;
    private readonly Dictionary<Sprite, (int Key, int Count)> _unbuilt = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Sprite> _seen = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Sprite> _dead = new(ReferenceEqualityComparer.Instance);
    private int _alive;

    public EnemyStock(Map map)
    {
        _map = map;
        Update();
    }

    public void Update()
    {
        foreach (var sprite in _map.Monsters)
        {
            if (sprite.Placement is null && _seen.Add(sprite) && IsCounted(sprite))
            {
                _alive++;
            }

            if (!sprite.IsAlive && _dead.Add(sprite) && IsCounted(sprite))
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
            foreach (var builder in _map.Builders)
            {
                count += CountUnbuilt(builder);
            }

            return count;
        }
    }

    private static bool IsCountedObject(KnownObject id) => KnownObject.Monsters.Contains(id) || KnownObject.Bosses.Contains(id);

    private static bool IsCounted(Sprite sprite) => sprite.Army == Army.Monsters && IsCountedObject(sprite.Definition.Id);

    private int CountUnbuilt(Sprite builder)
    {
        var pending = _map.GetPendingBuild(builder);
        int key = builder.ActionStack.Count * 2 + (pending is null ? 0 : 1);

        if (_unbuilt.TryGetValue(builder, out var cached) && cached.Key == key)
            return cached.Count;

        int count = (pending is not null && IsCountedObject(pending.Id) ? 1 : 0) + MeasureUnbuilt(builder.ActionStack.Orders);
        _unbuilt[builder] = (key, count);

        return count;
    }

    private static int MeasureUnbuilt(ReadOnlySpan<Act> orders)
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
