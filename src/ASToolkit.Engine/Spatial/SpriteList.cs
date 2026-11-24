using ASToolkit.Engine.IO;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a growable array of <see cref="Sprite"/>.
/// </summary>
public class SpriteList : MyList<Sprite>
{
    /// <summary>
    /// Adds a sprite and takes a reference to it.
    /// </summary>
    /// <param name="sprite">The sprite.</param>
    [EngineRoutine(0x0043de20)]
    public new void Insert(Sprite sprite)
    {
        sprite.AddRef();
        base.Insert(sprite);
    }

    /// <summary>
    /// Removes a sprite and drops the reference to it.
    /// </summary>
    /// <param name="sprite">The sprite.</param>
    /// <returns><see langword="true"/> if the list held the sprite; otherwise, <see langword="false"/>.</returns>
    [EngineRoutine(0x0043deb0)]
    public bool Delete(Sprite sprite)
    {
        for (int index = Count - 1; index >= 0; index--)
        {
            if (this[index] != sprite)
                continue;

            Count--;
            this[index] = this[Count];
            sprite.Map.Release(sprite);

            return true;
        }

        return false;
    }

    /// <summary>
    /// Removes the sprite at an index and destroys it.
    /// </summary>
    /// <param name="index">The index.</param>
    /// <returns>1 if the index is outside the list; otherwise, 0.</returns>
    [EngineRoutine(0x0043df50)]
    public int DeleteSpriteNumber(int index)
    {
        if (index < 0 || index >= Count)
            return 1;

        var sprite = this[index];
        Count--;
        this[index] = this[Count];
        int references = sprite.Release();
        if (references < 0)
        {
            sprite.Error?.Error($"SPRITE {(int)sprite.Vid.Id}", LogError.Invalid, references, "noRef at Release"u8);

            return 0;
        }

        sprite.Map.DeleteSprite(sprite);

        return 0;
    }

    /// <summary>
    /// Removes and destroys every sprite.
    /// </summary>
    [EngineRoutine(0x0043dfe0)]
    public void DeleteAll()
    {
        for (int i = 0; i < Count; i++)
        {
            for (int j = Count - 1; i < j; j--)
            {
                if (this[i] != this[j])
                    continue;

                this[j].Map.Release(this[j]);
                if (j < Count)
                {
                    Count--;
                    this[j] = this[Count];
                }
            }
        }

        for (int i = Count - 1; i >= 0; i--)
        {
            if (i < Count)
            {
                DeleteSpriteNumber(i);
            }
        }
    }

    /// <summary>
    /// Drops the reference to every sprite.
    /// </summary>
    [EngineRoutine(0x0043e0a0)]
    public new void Release()
    {
        for (int i = Count - 1; i >= 0; i--)
        {
            var sprite = this[i];
            int references = sprite.Release();
            if (references < 0)
            {
                sprite.Error?.Error($"SPRITE {(int)sprite.Vid.Id}", LogError.Invalid, references, "noRef at Release"u8);
            }
            else if (references == 0)
            {
                sprite.Map.DeleteSprite(sprite);
            }
            else if (i < Count)
            {
                Count--;
                this[i] = this[Count];
            }
        }
    }
}
