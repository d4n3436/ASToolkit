using ASToolkit.Engine.IO;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a list of sprites that holds a reference to each of its sprites.
/// </summary>
public sealed class SpriteList
{
    private const int GrowthStep = 4;

    private Sprite[] _items = [];

    /// <summary>
    /// Gets the number of sprites in the list.
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// Gets the sprite at an index.
    /// </summary>
    /// <param name="index">The index.</param>
    public Sprite this[int index] => _items[index];

    /// <summary>
    /// Returns the sprites of the list.
    /// </summary>
    /// <returns>The sprites, in the order the list holds them.</returns>
    public ReadOnlySpan<Sprite> AsSpan() => _items.AsSpan(0, Count);

    /// <summary>
    /// Adds a sprite at the end of the list and holds a reference to it.
    /// </summary>
    /// <param name="sprite">The sprite.</param>
    [EngineRoutine(0x0043de20)]
    public void Add(Sprite sprite)
    {
        sprite.AddRef();

        if (Count >= _items.Length)
        {
            Array.Resize(ref _items, _items.Length * 2 + GrowthStep);
        }

        _items[Count++] = sprite;
    }

    /// <summary>
    /// Removes the last occurrence of a sprite, moves the last sprite into its place and releases the reference to the removed sprite.
    /// Sprites left without references are deleted.
    /// </summary>
    /// <param name="sprite">The sprite.</param>
    /// <returns><see langword="true"/> if the list held the sprite; otherwise, <see langword="false"/>.</returns>
    [EngineRoutine(0x0043deb0)]
    public bool Remove(Sprite sprite)
    {
        for (int index = Count - 1; index >= 0; index--)
        {
            if (_items[index] != sprite)
                continue;

            Count--;
            _items[index] = _items[Count];

            int references = sprite.Release();

            if (references < 1)
            {
                if (references < 0)
                {
                    sprite.Logger?.Error($"SPRITE {(int)sprite.Definition.Id}", LogError.Invalid, references, "noRef at Release"u8);
                }
                else
                {
                    sprite.Map?.DeleteSprite(sprite);
                }
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Empties the list without releasing its sprites.
    /// </summary>
    public void Clear()
    {
        _items = [];
        Count = 0;
    }
}
