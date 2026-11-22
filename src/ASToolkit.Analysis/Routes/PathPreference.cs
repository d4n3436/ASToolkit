using System.Numerics;

namespace ASToolkit.Analysis.Routes;

internal sealed record PathPreference(Vector3 Target, Func<Vector3, bool> Accept, bool IsStrict);
