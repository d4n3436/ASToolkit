namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents an entry of the object table.
/// </summary>
/// <param name="Id">The object ID.</param>
/// <param name="Name">The Russian object name.</param>
/// <param name="Path">The sprite file of the object.</param>
/// <param name="Type">The engine object type.</param>
/// <param name="Flags">The object flags; 0x20 marks objects that block movement and 0x40 breakable ones.</param>
/// <param name="ExtentX">The collision extent along x.</param>
/// <param name="ExtentY">The collision extent along y.</param>
/// <param name="ExtentZ">The height.</param>
/// <param name="HitPoints">The hit points.</param>
/// <param name="Directions">The number of directions the sprite has frames for.</param>
/// <param name="FramesPerDirection">The number of frames of the first animation in each direction.</param>
public sealed record GameObject(KnownObject Id, string Name, string Path, uint Type, uint Flags, double ExtentX, double ExtentY, double ExtentZ, int HitPoints,
    int Directions, int FramesPerDirection)
{
    /// <summary>
    /// Returns the sprite frame the engine shows for a direction byte, out of the given number of frames.
    /// </summary>
    public int GetFrame(byte dir, int frameCount)
    {
        if (FramesPerDirection == 0)
        {
            return 0;
        }

        int index = ((dir + 128 / Directions) & 255) * Directions >> 8;
        return Math.Min(index * FramesPerDirection, frameCount - 1);
    }

    /// <summary>
    /// Gets the color shift applied to the sprite's palette per channel; a negative value darkens and a positive one adds.
    /// </summary>
    public (int R, int G, int B) Tint { get; init; }

    /// <summary>
    /// Gets a value indicating whether the object stops the player.
    /// </summary>
    public bool IsSolid => (Flags & 0x20) != 0;

    /// <summary>
    /// Gets a value indicating whether the object breaks when shot.
    /// </summary>
    public bool IsBreakable => (Flags & 0x40) != 0;
}
