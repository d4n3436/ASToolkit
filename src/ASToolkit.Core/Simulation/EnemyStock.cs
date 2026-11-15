using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Simulation;

internal sealed class EnemyStock
{
    private readonly GameWorld _world;
    private readonly Dictionary<ObjectProgram, (int Key, int Count)> _unbuilt = [];
    private readonly HashSet<Unit> _seen = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Unit> _dead = new(ReferenceEqualityComparer.Instance);
    private int _alive;

    public EnemyStock(GameWorld world)
    {
        _world = world;
        Update();
    }

    // Counts a monster by its kind when it first shows up and again when it dies
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
            foreach (var program in _world.Programs)
            {
                count += CountUnbuilt(program);
            }

            return count;
        }
    }

    private static bool IsCountedKind(KnownObject id) => KnownObject.Monsters.Contains(id) || KnownObject.Bosses.Contains(id);

    private static bool IsCounted(Unit unit) => unit.Army == Army.Monsters && IsCountedKind(unit.Kind.Id);

    private int CountUnbuilt(ObjectProgram program)
    {
        int key = program.Finished ? -1 : program.Cursor * 2 + (program.PendingBuild is null ? 0 : 1);

        if (_unbuilt.TryGetValue(program, out var cached) && cached.Key == key)
            return cached.Count;

        int count = MeasureUnbuilt(program);
        _unbuilt[program] = (key, count);

        return count;
    }

    private static int MeasureUnbuilt(ObjectProgram program)
    {
        var pending = program.PendingBuild;
        int count = pending is not null && IsCountedKind((KnownObject)pending.Value.Var1) ? 1 : 0;
        for (int index = program.Cursor - 1; index >= 0 && !program.Finished; index--)
        {
            var action = program.Actions[index];

            switch (action.Code)
            {
                case ActionCode.BuildUnit when IsCountedKind((KnownObject)action.Var1):
                    count++;
                    break;
                case ActionCode.GotoStack or ActionCode.StopStack or ActionCode.ClearStack:
                    return count;
            }
        }

        return count;
    }
}
