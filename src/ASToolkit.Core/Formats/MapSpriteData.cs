namespace ASToolkit.Core.Formats;

/// <summary>
/// Contains variable map sprite data.
/// </summary>
/// <param name="Handle">The sprite's handle.</param>
/// <param name="Payload">The payload.</param>
public readonly record struct MapSpriteData(uint Handle, ReadOnlyMemory<byte> Payload);
