namespace ASToolkit.Formats;

/// <summary>
/// Specifies the army of an object, the side it fights on.
/// </summary>
public enum Army : byte
{
    /// <summary>
    /// The player and most scenery.
    /// </summary>
    Player,

    /// <summary>
    /// The monsters and their generators.
    /// </summary>
    Monsters,

    /// <summary>
    /// Breakable objects, walls and corpses.
    /// </summary>
    Neutral,

    /// <summary>
    /// The guards friendly to the player in the later games.
    /// </summary>
    Guards,

    /// <summary>
    /// The hypnotized monsters in the later games.
    /// </summary>
    Hypnotized,

    /// <summary>
    /// The robots hostile to every other army in the later games.
    /// </summary>
    Robots,

    /// <summary>
    /// The monsters of looping generators in the later games, left out of the kill counts.
    /// </summary>
    Endless,

    /// <summary>
    /// An unknown (eighth) army in the later games.
    /// </summary>
    Eighth
}
