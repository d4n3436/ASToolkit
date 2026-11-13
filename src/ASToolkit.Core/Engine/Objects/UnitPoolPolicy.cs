using Microsoft.Extensions.ObjectPool;

namespace ASToolkit.Core.Engine;

// A unit another unit ever pointed at never returns to the pool
internal sealed class UnitPoolPolicy : PooledObjectPolicy<Unit>
{
    public override Unit Create() => new();

    public override bool Return(Unit unit) => !unit.IsReferenced;
}
