using System.Numerics;

namespace ASToolkit.Engine;

internal struct SpriteQuery : ISpriteQuery
{
    private readonly SpriteFilter _filter;
    private readonly Vector2 _point;
    private readonly float _previousDistance;
    private float _reach;

    public SpriteQuery(SpriteFilter filter, Vector2 point, float previousDistance, float reach)
    {
        _filter = filter;
        _point = point;
        _previousDistance = previousDistance;
        _reach = reach;
        Found = null;
    }

    public Sprite? Found { get; private set; }

    public void Consider(Sprite sprite, bool listed)
    {
        if (sprite.Owner is null)
        {
            ConsiderMenu(sprite);
        }
    }

    public void ConsiderMenu(Sprite sprite)
    {
        if (!_filter.Accepts(sprite))
            return;

        float distance = _point.MeasureOctagonalDistance(sprite.Position.AsVector2());

        if (distance < _reach && _previousDistance < distance)
        {
            _reach = distance;
            Found = sprite;
        }
    }
}
