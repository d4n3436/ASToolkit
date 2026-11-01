namespace ASToolkit.Core.Survey;

/// <summary>
/// Specifies the kind of furniture that fills most of a place.
/// </summary>
public enum PlaceFurnishing
{
    /// <summary>
    /// Fewer than three pieces in a room.
    /// </summary>
    Bare,

    /// <summary>
    /// Fewer than three pieces in a corridor.
    /// </summary>
    Passage,

    /// <summary>
    /// Tables, chairs, computers and paper.
    /// </summary>
    Office,

    /// <summary>
    /// Test tubes, flasks and lab equipment.
    /// </summary>
    Lab,

    /// <summary>
    /// Shelves, cabinets, crates and barrels.
    /// </summary>
    Storage,

    /// <summary>
    /// Containers, trucks and missiles.
    /// </summary>
    Depot,

    /// <summary>
    /// Transformers, electric panels and big computers.
    /// </summary>
    MachineRoom,

    /// <summary>
    /// Toilets, books, gym equipment and other home furniture.
    /// </summary>
    LivingQuarters,

    /// <summary>
    /// Plants and water cylinders.
    /// </summary>
    Lounge,

    /// <summary>
    /// Other pieces.
    /// </summary>
    Furnished
}
