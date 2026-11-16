using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents a read-only view of a menu file, a RES container of kind MENU.
/// </summary>
public readonly struct MenuResource
{
    private static readonly int SpriteSize = Unsafe.SizeOf<OldMapSprite>();
    private readonly ResContainer _container;

    /// <summary>
    /// Creates a menu over the bytes of its file.
    /// </summary>
    /// <param name="data">The bytes of the file.</param>
    public MenuResource(ReadOnlyMemory<byte> data)
    {
        _container = new ResContainer(data);
        Header = _container.TryGetChunk("HEAD"u8, out var head) ? MemoryMarshal.Read<MenuHeader>(head.Records.First().Span) : default;
    }

    /// <summary>
    /// Gets the header of the menu.
    /// </summary>
    public MenuHeader Header { get; }

    /// <summary>
    /// Gets the elements of the menu.
    /// </summary>
    public IEnumerable<MenuElement> Elements => EnumerateElements();

    private IEnumerable<MenuElement> EnumerateElements()
    {
        int version = Header.Version;
        if (_container.TryGetChunk("SPR "u8, out var sprites))
        {
            foreach (var record in sprites.Records)
            {
                if (record.Length >= SpriteSize && !IsEnd(record))
                {
                    yield return new MenuElement(record, version);
                }
            }

            yield break;
        }

        if (!_container.TryGetChunk("SPRI"u8, out var packed))
            yield break;

        var data = packed.Records.First();
        for (int offset = 0; offset + SpriteSize <= data.Length && !IsEnd(data[offset..]); offset += SpriteSize)
        {
            yield return new MenuElement(data.Slice(offset, SpriteSize), version);
        }
    }

    private static bool IsEnd(ReadOnlyMemory<byte> data) => MemoryMarshal.Read<int>(data.Span) == -1;
}
