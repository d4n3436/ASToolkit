namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents a flash of the whole screen during a playthrough.
/// </summary>
/// <param name="Time">The time the flash starts in milliseconds.</param>
/// <param name="Duration">The length of the flash in milliseconds.</param>
/// <param name="Color">The color the script asks for, as 0xRRGGBB.</param>
public readonly record struct ScreenFlash(int Time, int Duration, int Color);
