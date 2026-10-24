namespace ASToolkit.Core.Scripting;

/// <summary>
/// Represents a quest of the Alien Shooter 2 campaign.
/// </summary>
/// <param name="Line">The quest line.</param>
/// <param name="Sub">The number of the quest in its line.</param>
/// <param name="TimeLine">The position of the quest in the story.</param>
/// <param name="Title">The title the game shows, or the designer comment when the quest has no title.</param>
/// <param name="Items">The object events the quest reacts to.</param>
/// <param name="WinsOnBegin">A value indicating whether starting the quest wins the level.</param>
public sealed record Quest(int Line, int Sub, int TimeLine, string Title, IReadOnlyList<QuestItem> Items, bool WinsOnBegin)
{
    /// <summary>
    /// Gets the short name of the quest.
    /// </summary>
    public string Key => $"{Line}_{Sub}";

    /// <summary>
    /// Gets the quests that follow this one.
    /// </summary>
    public IReadOnlyList<(int Line, int Sub)> NextQuests { get; init; } = [];
}
