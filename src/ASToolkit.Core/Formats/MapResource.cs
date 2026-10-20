using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents a read-only view of a map.
/// </summary>
public readonly struct MapResource
{
    private readonly ResContainer _container;
    private readonly ReadOnlyMemory<byte> _rawSprites;

    /// <summary>
    /// Initializes a new instance of the <see cref="MapResource"/> structure.
    /// </summary>
    /// <param name="data">The data.</param>
    public MapResource(ReadOnlyMemory<byte> data)
    {
        _container = new ResContainer(data);
        var rawHeader = _container.Chunks.First(x => x.Tag.Span.SequenceEqual("HEAD"u8)).Records.First();
        
        Header = MemoryMarshal.Read<MapHeader>(rawHeader.Span);
        _rawSprites = _container.Chunks.First(x => x.Tag.Span.SequenceEqual("SPR "u8)).Records.First();
    }
    
    /// <summary>
    /// Gets the header.
    /// </summary>
    public MapHeader Header { get; }

    /// <summary>
    /// Gets the size in bytes the sprites in <see cref="Sprites"/>.
    /// </summary>
    /// <remarks>The size varies by map version.</remarks>
    public int SpriteSize => Header.Version > 12 ? 48 : 28;

    /// <summary>
    /// Enumerates over the placed sprites.
    /// </summary>
    public IEnumerable<MapSprite> Sprites => EnumerateSprites();

    /// <summary>
    /// Enumerates over the sprite data.
    /// </summary>
    public IEnumerable<MapSpriteData> SpriteData => EnumerateSpriteData();

    private IEnumerable<MapSprite> EnumerateSprites()
    {
        for (int index = 0; index < (_rawSprites.Length - 4) / SpriteSize; index++)
        {
            var rawSprite = _rawSprites.Slice(index * SpriteSize, SpriteSize);
            var sprite = MemoryMarshal.Read<MapSprite>(rawSprite.Span);

            yield return Header.Version >= 12 ? sprite : new MapSprite(sprite, true);
        }
    }
    
    private IEnumerable<MapSpriteData> EnumerateSpriteData()
    {
        foreach (var record in _container.Chunks.First(x => x.Tag.Span.SequenceEqual("SPRD"u8)).Records)
        {
            if (record.Length > 4)
            {
                yield return new MapSpriteData(MemoryMarshal.Read<uint>(record.Span), record[4..]);
            }
        }
    }
}
