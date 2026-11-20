using System.Collections;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Simulation;

/// <summary>
/// Represents the sprites of a map that match a filter, in update order.
/// </summary>
public struct SpriteView : IEnumerable<Sprite>, IEnumerator<Sprite>
{
    private readonly Map _map;
    private readonly SpriteViewKind _kind;
    private int _layer;
    private int _index;

    internal SpriteView(Map map, SpriteViewKind kind)
    {
        _map = map;
        _kind = kind;
        _index = -1;
        Current = null!;
    }

    /// <summary>
    /// Gets the current sprite.
    /// </summary>
    public Sprite Current { get; private set; }

    readonly object IEnumerator.Current => Current;

    /// <summary>
    /// Returns an enumerator over the sprites, starting before the first one.
    /// </summary>
    /// <returns>The enumerator.</returns>
    public readonly SpriteView GetEnumerator() => new(_map, _kind);

    readonly IEnumerator<Sprite> IEnumerable<Sprite>.GetEnumerator() => GetEnumerator();

    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Advances to the next sprite.
    /// </summary>
    /// <returns><see langword="true"/> if there is a next sprite; otherwise, <see langword="false"/>.</returns>
    public bool MoveNext()
    {
        if (_kind == SpriteViewKind.Monsters)
            return MoveNextMonster();

        while (_layer < UpdateOrder.LayerCount)
        {
            var list = _map.GetLayer(_layer);

            while (++_index < list.Count)
            {
                var sprite = list[_index];

                if (sprite is not null && Matches(sprite))
                {
                    Current = sprite;

                    return true;
                }
            }

            _layer++;
            _index = -1;
        }

        return false;
    }

    readonly void IDisposable.Dispose()
    {
    }

    void IEnumerator.Reset()
    {
        _layer = 0;
        _index = -1;
    }

    private bool MoveNextMonster()
    {
        while (++_index < _map.Collector.Listed.Length)
        {
            var sprite = _map.Collector.Listed[_index];
            var spriteClass = sprite.Definition.Object.SpriteClass;

            if (!sprite.Definition.Id.IsScripted && spriteClass != SpriteClass.Man && spriteClass != SpriteClass.Building && sprite.Owner?.Definition.Object.SpriteClass != SpriteClass.Man)
            {
                Current = sprite;

                return true;
            }
        }

        return false;
    }

    private readonly bool Matches(Sprite sprite) => _kind switch
    {
        SpriteViewKind.Active => (sprite.Placement is null || sprite.Definition.Id.IsScripted) && sprite.Definition.Object.SpriteClass is not (SpriteClass.Frame or SpriteClass.Text) &&
            sprite.Definition.Id != SpriteCatalog.HardwareGroundId,
        SpriteViewKind.Scripted => sprite.Definition.Id.IsScripted,
        _ => true
    };
}
