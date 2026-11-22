namespace ASToolkit.Formats;

/// <summary>
/// Provides conversions between the sprite types of Alien Shooter and of the later games.
/// </summary>
public static class SpriteTypeExtensions
{
    private const int ShiftedTypeMask = 0x7F;
    private const int SharedTypeMask = ~0xFF;

    /// <summary>
    /// Converts a sprite type in the Alien Shooter numbering to the numbering of the later games.
    /// </summary>
    /// <param name="type">The sprite type in the Alien Shooter numbering.</param>
    /// <returns>The sprite type in the numbering of the later games.</returns>
    public static SpriteType ToSpriteType(this OldSpriteType type) =>
        (SpriteType)((((int)type & ShiftedTypeMask) << 1) | ((int)type & SharedTypeMask));

    /// <summary>
    /// Converts a sprite type in the numbering of the later games to the Alien Shooter numbering.
    /// </summary>
    /// <param name="type">The sprite type in the numbering of the later games.</param>
    /// <returns>The sprite type in the Alien Shooter numbering.</returns>
    public static OldSpriteType ToOldSpriteType(this SpriteType type) =>
        (OldSpriteType)((((int)type >> 1) & ShiftedTypeMask) | ((int)type & SharedTypeMask));
}
