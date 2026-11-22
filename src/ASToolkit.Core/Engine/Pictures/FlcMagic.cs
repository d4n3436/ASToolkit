namespace ASToolkit.Core.Engine;

/// <summary>
/// Specifies the file type field of an FLC animation file header.
/// </summary>
public enum FlcMagic : ushort
{
    /// <summary>
    /// The file is an FLI animation, whose speed is counted in 1/70 seconds and whose first frame follows the header.
    /// </summary>
    Fli = 0xaf11,

    /// <summary>
    /// The file is an FLC animation, whose speed is counted in milliseconds and which holds the offset of its first frame.
    /// </summary>
    Flc = 0xaf12
}
