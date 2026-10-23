using System.Numerics;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents one placed object of a map with its payload.
/// </summary>
/// <param name="Handle">The sprite handle that links the entry to its payload.</param>
/// <param name="Id">The object ID in objects.res.</param>
/// <param name="Position">The position in map pixels, with the height above the floor.</param>
/// <param name="Dir">The facing field.</param>
/// <param name="Army">The army of the object.</param>
/// <param name="Payload">The object's payload bytes.</param>
/// <param name="NegativeTint">The negative part of the tint of the placed object.</param>
/// <param name="PositiveTint">The positive part of the tint of the placed object.</param>
public sealed record MergedMapSprite(uint Handle, KnownObject Id, Vector3 Position, uint Dir, uint Army, byte[] Payload,
    uint NegativeTint = 0, uint PositiveTint = 0)
{
    /// <summary>
    /// Gets the scale of the sprite along x, y and the height.
    /// </summary>
    public Vector3 Scale { get; init; } = Vector3.One;

    /// <summary>
    /// Gets the facing the engines use, the low byte of <see cref="Dir"/>.
    /// </summary>
    public byte Facing => (byte)Dir;
}
