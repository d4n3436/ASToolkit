namespace ASToolkit.Core.Generation.Layout;

/// <summary>
/// Represents the room sizes and connections a <see cref="LayoutStyle"/> builds with.
/// </summary>
/// <param name="SizeScale">The scale on the theme's room size.</param>
/// <param name="BigRoom">The size of a big room, in units.</param>
/// <param name="BigRoomChance">The chance that a room is a big room.</param>
/// <param name="PrefersWideLinks">A value indicating whether rooms connect mostly through arches and openings.</param>
public readonly record struct LayoutStyleProfile(
    float SizeScale,
    (int Min, int Max) BigRoom,
    float BigRoomChance,
    bool PrefersWideLinks)
{
    /// <summary>
    /// Represents the room complex style, with the theme's room sizes and connections.
    /// </summary>
    public static readonly LayoutStyleProfile Room = new(1, (0, 0), 0, false);

    /// <summary>
    /// Represents the open style, with bigger rooms, frequent big rooms and mostly wide connections.
    /// </summary>
    public static readonly LayoutStyleProfile Open = new(2.4f, (28, 44), 0.5f, true);

    /// <summary>
    /// Returns the profile of a style.
    /// </summary>
    public static LayoutStyleProfile FromStyle(LayoutStyle style) => style == LayoutStyle.Open ? Open : Room;
}
