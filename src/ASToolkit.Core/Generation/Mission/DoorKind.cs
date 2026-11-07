namespace ASToolkit.Core.Generation.Mission;

/// <summary>
/// Specifies what a door the doors stage lays between two touching rooms is.
/// </summary>
public enum DoorKind
{
    /// <summary>A door inside one phase, open from the start of the phase.</summary>
    Door,

    /// <summary>A locked door between two phases that its key opens, or the door a blackout closes behind the player.</summary>
    Lock,

    /// <summary>A locked door between two rooms of the phase that holds the key, opened by the key.</summary>
    Shortcut
}
