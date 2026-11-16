using System.Numerics;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents a group of units the player commands together.
/// </summary>
public sealed class UnitGroup
{
    private const float CenterBlend = 0.5f;
    private readonly List<Unit> _members = [];

    /// <summary>
    /// Creates a group with its first member.
    /// </summary>
    /// <param name="first">The first member.</param>
    [EngineRoutine(0x0043ab90)]
    public UnitGroup(Unit first)
    {
        Add(first);
    }

    /// <summary>
    /// Gets the members of the group.
    /// </summary>
    public IReadOnlyList<Unit> Members => _members;

    /// <summary>
    /// Gets the center of the group's members.
    /// </summary>
    public Vector2 Center { get; private set; }

    /// <summary>
    /// Adds a member.
    /// </summary>
    /// <param name="unit">The member.</param>
    [EngineRoutine(0x0043aca0)]
    public void Add(Unit unit)
    {
        var position = unit.Position.AsVector2();
        Center = _members.Count != 0 ? (position + Center) * CenterBlend : position;
        _members.Add(unit);
    }

    /// <summary>
    /// Removes a member, moving the last member into its place, and returns whether it was in the group.
    /// </summary>
    /// <param name="unit">The member.</param>
    /// <returns><see langword="true"/> when the unit was removed; otherwise, <see langword="false"/>.</returns>
    [EngineRoutine(0x0043deb0)]
    public bool Remove(Unit unit)
    {
        for (int i = _members.Count - 1; i >= 0; i--)
        {
            if (_members[i] != unit)
                continue;

            _members[i] = _members[^1];
            _members.RemoveAt(_members.Count - 1);

            return true;
        }

        return false;
    }
}
