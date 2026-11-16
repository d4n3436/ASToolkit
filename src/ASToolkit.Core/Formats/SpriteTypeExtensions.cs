namespace ASToolkit.Core.Formats;

internal static class SpriteTypeExtensions
{
    private const uint ShiftedTypeMask = 0x7F;
    private const uint SharedTypeMask = ~0xFFu;

    public static SpriteType ToSpriteType(this OldSpriteType type) =>
        (SpriteType)((((uint)type & ShiftedTypeMask) << 1) | ((uint)type & SharedTypeMask));

    public static OldSpriteType ToOldSpriteType(this SpriteType type) =>
        (OldSpriteType)((((uint)type >> 1) & ShiftedTypeMask) | ((uint)type & SharedTypeMask));
}
