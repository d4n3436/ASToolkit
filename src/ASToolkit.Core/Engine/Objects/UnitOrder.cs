using System.Numerics;

namespace ASToolkit.Core.Engine;

// An entry whose target was deleted has neither a point nor a target
internal readonly record struct UnitOrder(Vector2? Point, Unit? Target);
