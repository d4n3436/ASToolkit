using System.Numerics;

namespace ASToolkit.Core.Engine;

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

    public Unit? Found { get; private set; }

    [EngineRoutine(0x00410670)]
    public void Consider(Unit sprite, bool listed)
    {
        if (sprite.Owner is not null || !_filter.Accepts(sprite))
            return;

        float distance = _point.MeasureOctagonalDistance(sprite.Position.AsVector2());

        if (distance < _reach && _previousDistance < distance)
        {
            _reach = distance;
            Found = sprite;
        }
    }
}
