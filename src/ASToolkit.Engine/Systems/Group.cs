using System.Numerics;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a group of units the player commands together.
/// </summary>
public sealed class Group : SpriteList
{
    private const float CenterBlend = 0.5f;

    /// <summary>
    /// Initializes a new instance of the <see cref="Group"/> class and links it into the circle of groups after another group.
    /// </summary>
    /// <param name="previous">The group the new group follows, or <see langword="null"/> to start a circle of its own.</param>
    /// <param name="first">The first member, or <see langword="null"/> to start the group without one.</param>
    [EngineRoutine(0x0043ab90)]
    public Group(Group? previous, Sprite? first)
    {
        if (previous is null)
        {
            Next = this;
        }
        else
        {
            Next = previous.Next;
            previous.Next = this;
        }

        if (first is not null)
        {
            Add(first);
        }
    }

    /// <summary>
    /// Gets the group that follows this group in the circle of groups.
    /// </summary>
    public Group Next { get; private set; }

    /// <summary>
    /// Gets the center of the group's members.
    /// </summary>
    public Vector2 Center { get; private set; }

    /// <summary>
    /// Takes the group out of the circle of groups and drops its member list.
    /// </summary>
    [EngineRoutine(0x0043abf0)]
    public void Unlink()
    {
        var next = Next;
        var previous = next;

        for (var group = next.Next; group != this; group = group.Next)
        {
            previous = group;
        }

        previous.Next = next;
        Count = 0;
    }

    /// <summary>
    /// Adds a member.
    /// </summary>
    /// <param name="sprite">The member.</param>
    [EngineRoutine(0x0043aca0)]
    public void Add(Sprite sprite)
    {
        var position = sprite.Position.AsVector2();
        Center = Count != 0 ? (position + Center) * CenterBlend : position;
        Insert(sprite);
    }

    [EngineRoutine(0x0043ac40)]
    internal void Draw(int index)
    {
        foreach (var member in this)
        {
            member.Map.Session.Graphics.DrawFormattedText(member.Map.GetScreenPoint(member), $"{index}");
        }
    }
}
