namespace ASToolkit.Core.Survey;

/// <summary>
/// Specifies where in its place a point lies.
/// </summary>
public enum PlaceSpot
{
    /// <summary>
    /// A place the route never enters.
    /// </summary>
    OffRoute,

    /// <summary>
    /// A place shorter than 250 map pixels.
    /// </summary>
    SmallPlace,

    /// <summary>
    /// The third nearest the entrance.
    /// </summary>
    NearEntrance,

    /// <summary>
    /// The middle third.
    /// </summary>
    Middle,

    /// <summary>
    /// The third furthest from the entrance.
    /// </summary>
    FarEnd
}
