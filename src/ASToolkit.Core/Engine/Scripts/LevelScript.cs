using ASToolkit.Core.Scripting;

namespace ASToolkit.Core.Engine;

internal sealed class LevelScript
{
    public int LevelNumber { get; init; }

    public bool DoorsLocked { get; init; }

    public bool ClearsMonsters { get; init; }

    public TriggerArea? Exit { get; init; }

    public TriggerArea? Blackout { get; init; }

    public IReadOnlyList<TriggerArea> Checkpoints { get; init; } = [];
}
