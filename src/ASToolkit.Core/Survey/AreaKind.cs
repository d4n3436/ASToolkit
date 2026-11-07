namespace ASToolkit.Core.Survey;

/// <summary>
/// Specifies the kind of an area, a group of rooms with one job that the player recognizes as one place.
/// </summary>
public enum AreaKind
{
    /// <summary>A hall or corridor with rooms opening on one or both sides.</summary>
    HallWithRooms,

    /// <summary>A few rooms opening into one another, with no hall.</summary>
    Suite,

    /// <summary>Small rooms side by side along one wall, each opening onto the next.</summary>
    Row,

    /// <summary>Rooms joined in a loop around an empty court or a yard.</summary>
    Ring,

    /// <summary>Big rooms wall to wall, with small rooms standing inside them.</summary>
    DenseBlock,

    /// <summary>One hangar, open-plan office or depot, with or without side rooms opening on it.</summary>
    BigRoom,

    /// <summary>A yard with bays or buildings opening on it from several sides.</summary>
    Yard
}
