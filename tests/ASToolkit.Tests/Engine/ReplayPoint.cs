using System.Numerics;

namespace ASToolkit.Tests.Engine;

internal sealed record ReplayPoint(int Time, Vector3 Position, float Speed, int Health, int RandomState);
