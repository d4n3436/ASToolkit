using System.ComponentModel;
using System.Numerics;

namespace ASToolkit.Cli.Commands;

[TypeConverter(typeof(MapAreaConverter))]
internal readonly record struct MapArea(Vector2 Center, float Radius)
{
    public bool Contains(Vector2 point) => Vector2.Distance(Center, point) <= Radius;
}
