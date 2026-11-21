namespace ASToolkit.Core.Scripting;

/// <summary>
/// Represents an object event a quest reacts to in one of its states.
/// </summary>
/// <param name="State">The quest state, such as QS_BEGUN.</param>
/// <param name="Event">The object event, such as CSI_DEATH or CSI_TAKE.</param>
/// <param name="Name">The name of the sprite or item, or the object ID as text.</param>
public sealed record QuestItem(string State, string Event, string Name);
