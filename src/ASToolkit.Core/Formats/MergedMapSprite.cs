namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents one placed object of a map with its payload.
/// </summary>
/// <param name="Handle">The sprite handle that links the entry to its payload.</param>
/// <param name="Id">The object ID in objects.res.</param>
/// <param name="X">The x position in map pixels.</param>
/// <param name="Y">The y position in map pixels.</param>
/// <param name="Z">The height above the floor; negative values raise built-in doors.</param>
/// <param name="Dir">The facing field.</param>
/// <param name="Side">The side of the object.</param>
/// <param name="Payload">The object's payload bytes.</param>
/// <param name="NegativeTint">The negative part of the tint of the placed object.</param>
/// <param name="PositiveTint">The positive part of the tint of the placed object.</param>
/// <param name="ScaleX">The scale of the sprite along x.</param>
/// <param name="ScaleY">The scale of the sprite along y.</param>
/// <param name="ScaleZ">The scale of the sprite along the height.</param>
public sealed record MergedMapSprite(uint Handle, KnownObject Id, float X, float Y, float Z, uint Dir, uint Side, byte[] Payload,
    uint NegativeTint = 0, uint PositiveTint = 0, float ScaleX = 1, float ScaleY = 1, float ScaleZ = 1)
{
    /// <summary>
    /// Gets the facing the engines use, the low byte of <see cref="Dir"/>.
    /// </summary>
    public byte Facing => (byte)Dir;
}
