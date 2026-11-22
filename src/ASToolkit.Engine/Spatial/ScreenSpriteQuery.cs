using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

internal struct ScreenSpriteQuery : ISpriteQuery
{
    private readonly SpriteFilter _filter;
    private readonly Vector2 _point;

    public ScreenSpriteQuery(SpriteFilter filter, Vector2 point)
    {
        _filter = filter;
        _point = point;
        Found = null;
    }

    public Sprite? Found { get; private set; }

    public void Consider(Sprite sprite, bool listed)
    {
        if ((!listed && sprite.Owner is not null) || !_filter.Accepts(sprite))
            return;

        var definition = sprite.Definition;
        var half = definition.Half;
        var size = definition.Object.Size;
        float top = sprite.Position.Y - sprite.Position.Z;

        if (!listed && !_filter.Search.HasFlag(SpriteSearch.Hash) && _filter.Types.HasFlag(OldSpriteType.Region) &&
            definition.Object.SpriteClass == SpriteClass.Region)
        {
            half = sprite is RegionSprite region ? region.Size * 0.5f : Vector2.Zero;
        }

        TakeIfHit(sprite, half, size, top);
    }

    public void ConsiderMenu(Sprite sprite)
    {
        if (_filter.Accepts(sprite))
        {
            TakeIfHit(sprite, sprite.Definition.Half, sprite.Definition.Object.Size, sprite.Position.Y - sprite.Position.Z);
        }
    }

    private void TakeIfHit(Sprite sprite, Vector2 half, Vector3 size, float top)
    {
        if (sprite.Position.X - half.X <= _point.X && _point.X <= sprite.Position.X + half.X &&
            top - size.Z - half.Y < _point.Y && _point.Y < top + half.Y &&
            (Found is null || size.X < Found.Definition.Object.Size.X || size.Y < Found.Definition.Object.Size.Y))
        {
            Found = sprite;
        }
    }
}
