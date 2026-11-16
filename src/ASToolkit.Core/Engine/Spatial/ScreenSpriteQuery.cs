using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

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

    public Unit? Found { get; private set; }

    [EngineRoutine(0x0040fc40)]
    public void Consider(Unit sprite, bool listed)
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
            // Unused on AS1
            half = sprite.RegionSize * 0.5f;
        }

        if (sprite.Position.X - half.X <= _point.X && _point.X <= sprite.Position.X + half.X &&
            top - size.Z - half.Y < _point.Y && _point.Y < top + half.Y &&
            (Found is null || size.X < Found.Definition.Object.Size.X || size.Y < Found.Definition.Object.Size.Y))
        {
            Found = sprite;
        }
    }
}
