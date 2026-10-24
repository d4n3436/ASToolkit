using System.Numerics;

namespace ASToolkit.Core.Routes;

// Chooses where a path into an area ends, the reachable cell nearest to a target that the accept test allows
internal sealed record PathPreference(Vector3 Target, Func<Vector3, bool> Accept, bool IsStrict);
