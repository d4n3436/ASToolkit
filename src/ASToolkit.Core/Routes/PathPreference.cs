using System.Numerics;

namespace ASToolkit.Core.Routes;

internal sealed record PathPreference(Vector3 Target, Func<Vector3, bool> Accept, bool IsStrict);
