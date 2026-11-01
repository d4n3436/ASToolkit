namespace ASToolkit.Core.Survey;

/// <summary>
/// Specifies what a place is for in play.
/// </summary>
public enum PlaceUse
{
    /// <summary>
    /// No particular use.
    /// </summary>
    None,

    /// <summary>
    /// A secret closet that holds items.
    /// </summary>
    Stash,

    /// <summary>
    /// The room of a boss.
    /// </summary>
    BossDen,

    /// <summary>
    /// The room of the breaker.
    /// </summary>
    BreakerRoom,

    /// <summary>
    /// The room of a generator.
    /// </summary>
    GeneratorRoom,

    /// <summary>
    /// The room of a dynamite site.
    /// </summary>
    DynamiteSite,

    /// <summary>
    /// A place that monsters come out of.
    /// </summary>
    MonsterSource,

    /// <summary>
    /// A place that holds a large share of the placed monsters.
    /// </summary>
    Lair
}
