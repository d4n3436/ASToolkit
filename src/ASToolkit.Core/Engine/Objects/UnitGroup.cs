using System.Numerics;

namespace ASToolkit.Core.Engine;

internal sealed class UnitGroup
{
    private const float CenterBlend = 0.5f;
    private readonly List<Unit> _members = [];

    [EngineRoutine(0x0043ab90)]
    public UnitGroup(Unit first)
    {
        Add(first);
    }

    public IReadOnlyList<Unit> Members => _members;

    public Vector2 Center { get; private set; }

    [EngineRoutine(0x0043aca0)]
    public void Add(Unit unit)
    {
        var position = unit.Position.AsVector2();
        Center = _members.Count != 0 ? (position + Center) * CenterBlend : position;
        _members.Add(unit);
    }

    // Swaps the last member into the removed member's place
    [EngineRoutine(0x0043deb0)]
    public bool Remove(Unit unit)
    {
        for (int index = _members.Count - 1; index >= 0; index--)
        {
            if (_members[index] != unit)
                continue;

            _members[index] = _members[^1];
            _members.RemoveAt(_members.Count - 1);

            return true;
        }

        return false;
    }
}
