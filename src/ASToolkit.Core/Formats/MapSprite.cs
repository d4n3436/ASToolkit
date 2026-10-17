namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents one placed object of a map with its payload.
/// </summary>
/// <param name="Handle">The sprite handle that links the entry to its payload.</param>
/// <param name="Id">The object ID in objects.res.</param>
/// <param name="X">The x position in map pixels.</param>
/// <param name="Y">The y position in map pixels.</param>
/// <param name="Z">The height above the floor; negative values raise built-in doors.</param>
/// <param name="Dir">The facing, 0 to 255.</param>
/// <param name="Army">The side; 1 for enemies, 2 for breakable objects, 0 for the rest.</param>
/// <param name="Payload">The object's payload bytes.</param>
public sealed record MapSprite(uint Handle, KnownObject Id, double X, double Y, double Z, byte Dir, uint Army, byte[] Payload);
