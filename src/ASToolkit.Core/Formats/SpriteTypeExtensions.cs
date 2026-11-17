namespace ASToolkit.Core.Formats;

internal static class SpriteTypeExtensions
{
    private const int ShiftedTypeMask = 0x7F;
    private const int SharedTypeMask = ~0xFF;

    public static SpriteType ToSpriteType(this OldSpriteType type) =>
        (SpriteType)((((int)type & ShiftedTypeMask) << 1) | ((int)type & SharedTypeMask));

    public static OldSpriteType ToOldSpriteType(this SpriteType type) =>
        (OldSpriteType)((((int)type >> 1) & ShiftedTypeMask) | ((int)type & SharedTypeMask));
}
