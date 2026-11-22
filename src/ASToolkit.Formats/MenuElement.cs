using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

/// <summary>
/// Represents a read-only view of an element of a menu, its sprite followed by the fields the sprite's class loads.
/// </summary>
public readonly struct MenuElement
{
    private const int ItemsVersion = 12;
    private static readonly int SpriteSize = Unsafe.SizeOf<OldMapSprite>();
    private static readonly int ActionSize = Unsafe.SizeOf<StackAction>();

    private readonly ReadOnlyMemory<byte> _data;
    private readonly int _version;

    /// <summary>
    /// Creates an element over the bytes of its SPR record.
    /// </summary>
    /// <param name="data">The bytes of the record.</param>
    /// <param name="version">The version of the menu.</param>
    public MenuElement(ReadOnlyMemory<byte> data, int version)
    {
        _data = data;
        _version = version;
    }

    /// <summary>
    /// Gets the sprite of the element, in menu pixels.
    /// </summary>
    public MapSprite Sprite => new(MemoryMarshal.AsRef<OldMapSprite>(_data.Span), _version < MapHeader.FloatVersion);

    /// <summary>
    /// Gets the orders of the element's sprite, empty for packed elements.
    /// </summary>
    public ReadOnlySpan<StackAction> Orders => IsPacked ? [] : MemoryMarshal.Cast<byte, StackAction>(_data.Span.Slice(SpriteSize + sizeof(int), OrderCount * ActionSize));

    /// <summary>
    /// Gets the object IDs of the items the element's sprite holds, empty before version 12 and for packed elements.
    /// </summary>
    public ReadOnlySpan<int> Items
    {
        get
        {
            if (_version < ItemsVersion || IsPacked)
                return [];

            var span = _data.Span[ItemsStart..];
            return MemoryMarshal.Cast<byte, int>(span.Slice(sizeof(int), MemoryMarshal.Read<int>(span) * sizeof(int)));
        }
    }

    /// <summary>
    /// Gets the bytes the sprite's class loads after the items.
    /// </summary>
    public ReadOnlySpan<byte> ClassFields
    {
        get
        {
            if (IsPacked)
                return [];

            int start = ItemsStart;
            if (_version >= ItemsVersion)
            {
                start += sizeof(int) + MemoryMarshal.Read<int>(_data.Span[start..]) * sizeof(int);
            }

            return _data.Span[start..];
        }
    }

    /// <summary>
    /// Gets the behavior of a text element.
    /// </summary>
    public TextBehavior TextBehavior => ClassFields.Length < sizeof(int) ? default : MemoryMarshal.Read<TextBehavior>(ClassFields);

    /// <summary>
    /// Gets the text of a text element, in cp1251 encoding.
    /// </summary>
    public ReadOnlySpan<byte> Text
    {
        get
        {
            var fields = ClassFields;
            if (fields.Length < sizeof(int))
                return [];

            var text = fields[sizeof(int)..];
            int end = text.IndexOf((byte)0);
            return end < 0 ? text : text[..end];
        }
    }

    private bool IsPacked => _data.Length <= SpriteSize;

    private int OrderCount => IsPacked ? 0 : MemoryMarshal.Read<int>(_data.Span[SpriteSize..]);

    private int ItemsStart => SpriteSize + sizeof(int) + OrderCount * ActionSize;
}
