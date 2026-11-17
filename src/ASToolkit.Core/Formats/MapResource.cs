using System.Runtime.CompilerServices;
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

        foreach (var chunk in _container.Chunks.Where(x => x.Tag.Span.SequenceEqual("GRPH"u8)))
        {
            Graphics = MemoryMarshal.Read<MapGraphics>(chunk.Records.First().Span);
            return;
        }
    }
 
    /// <summary>
    /// Gets the header.
    /// </summary>
    public MapHeader Header { get; }

    /// <summary>
    /// Gets the graphics record.
    /// </summary>
    public MapGraphics Graphics { get; }

    /// <summary>
    /// Gets the size (in bytes) of the sprites on <see cref="Sprites"/>.
    /// </summary>
    /// <remarks>The size varies by map version.</remarks>
    public int SpriteSize => Header.Version >= 16 ? Unsafe.SizeOf<MapSprite>() : Unsafe.SizeOf<OldMapSprite>();

    /// <summary>
    /// Enumerates over the placed sprites.
    /// </summary>
    public IEnumerable<MapSprite> Sprites => EnumerateSprites();

    /// <summary>
    /// Enumerates over the sprite data.
    /// </summary>
    public IEnumerable<MapSpriteData> SpriteData => EnumerateSpriteData();

    /// <summary>
    /// Enumerates over the groups of sprites, each a span of sprite handles.
    /// </summary>
    public MapGroupEnumerator Groups => new(MemoryMarshal.Cast<byte, int>(GetRecord("GROU"u8).Span));

    /// <summary>
    /// Gets the sprite handle of each player, empty when the map has no PLAY chunk.
    /// </summary>
    public ReadOnlySpan<int> PlayerHandles => MemoryMarshal.Cast<byte, int>(GetRecord("PLAY"u8).Span);

    /// <summary>
    /// Gets the painted height grid, empty when the map has no GRID chunk.
    /// </summary>
    public ReadOnlySpan<short> HeightGrid => MemoryMarshal.Cast<byte, short>(GetRecord("GRID"u8).Span);

    private IEnumerable<MapSprite> EnumerateSprites()
    {
        for (int index = 0; index < (_rawSprites.Length - 4) / SpriteSize; index++)
        {
            var rawSprite = _rawSprites.Slice(index * SpriteSize, SpriteSize);
            yield return SpriteSize == Unsafe.SizeOf<MapSprite>()
                ? MemoryMarshal.Read<MapSprite>(rawSprite.Span)
                : new MapSprite(MemoryMarshal.Read<OldMapSprite>(rawSprite.Span), Header.Version < 10);
        }
    }
    
    private ReadOnlyMemory<byte> GetRecord(ReadOnlySpan<byte> tag) => _container.TryGetChunk(tag, out var chunk) ? chunk.Records.First() : ReadOnlyMemory<byte>.Empty;

    private IEnumerable<MapSpriteData> EnumerateSpriteData()
    {
        foreach (var record in _container.Chunks.First(x => x.Tag.Span.SequenceEqual("SPRD"u8)).Records)
        {
            if (record.Length > 4)
            {
                yield return new MapSpriteData(MemoryMarshal.Read<int>(record.Span), record[4..]);
            }
        }
    }
}
