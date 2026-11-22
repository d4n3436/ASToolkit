using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a group of units the player commands together.
/// </summary>
public sealed class Group
{
    private const float CenterBlend = 0.5f;
    private readonly List<Sprite> _members = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="Group"/> class with its first member.
    /// </summary>
    /// <param name="first">The first member.</param>
    [EngineRoutine(0x0043ab90)]
    public Group(Sprite first)
    {
        Add(first);
    }

    /// <summary>
    /// Gets the members of the group.
    /// </summary>
    public IReadOnlyList<Sprite> Members => _members;

    /// <summary>
    /// Gets the center of the group's members.
    /// </summary>
    public Vector2 Center { get; private set; }

    /// <summary>
    /// Adds a member.
    /// </summary>
    /// <param name="sprite">The member.</param>
    [EngineRoutine(0x0043aca0)]
    public void Add(Sprite sprite)
    {
        var position = sprite.Position.AsVector2();
        Center = _members.Count != 0 ? (position + Center) * CenterBlend : position;
        _members.Add(sprite);
        sprite.AddRef();
    }

    /// <summary>
    /// Removes a member from the group and moves the last member into its place.
    /// </summary>
    /// <param name="sprite">The member.</param>
    /// <returns><see langword="true"/> if the sprite was in the group and was removed; otherwise, <see langword="false"/>.</returns>
    public bool Remove(Sprite sprite)
    {
        for (int i = _members.Count - 1; i >= 0; i--)
        {
            if (_members[i] != sprite)
                continue;

            _members[i] = _members[^1];
            _members.RemoveAt(_members.Count - 1);

            return true;
        }

        return false;
    }
}
