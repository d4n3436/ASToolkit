namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents one vertex of the depth mesh of a cached sprite frame.
/// </summary>
public readonly struct VidVertex
{
    private const int FieldMask = 0x3ff;
    private const int YShift = 10;
    private const int ZShift = 20;
    private const int ScaleShift = 30;
    private const float ZOffset = 32;

    private readonly uint _packed;

    /// <summary>
    /// Initializes a new instance of the <see cref="VidVertex"/> structure.
    /// </summary>
    /// <param name="packed">The packed vertex.</param>
    public VidVertex(uint packed)
    {
        _packed = packed;
    }

    /// <summary>
    /// Gets the column on the canvas.
    /// </summary>
    public int X => (int)(_packed & FieldMask);

    /// <summary>
    /// Gets the row on the canvas.
    /// </summary>
    public int Y => (int)(_packed >> YShift & FieldMask);

    /// <summary>
    /// Gets the height of the vertex above the base of the sprite.
    /// </summary>
    public float Z => (_packed >> ZShift & FieldMask) / (float)(1 << (int)(_packed >> ScaleShift)) - ZOffset;
}
