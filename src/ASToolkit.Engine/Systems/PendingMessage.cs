using System.Numerics;

namespace ASToolkit.Engine;

internal readonly record struct PendingMessage(ReadOnlyMemory<byte> Text, Vector2 Target, int Time);
