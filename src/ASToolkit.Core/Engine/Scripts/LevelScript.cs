using ASToolkit.Core.Scripting;

namespace ASToolkit.Core.Engine;

internal sealed class LevelScript
{
    public int LevelNumber { get; init; }

    public bool DoorsLocked { get; init; }

    public bool ClearsMonsters { get; init; }

    public bool WinsAfterSites { get; init; }

    public IReadOnlyList<ScriptTrigger> Triggers { get; init; } = [];

    public IReadOnlyList<ScriptHandler> Handlers { get; init; } = [];

    public IReadOnlyList<ScriptCrowdRule> CrowdRules { get; init; } = [];
}
