using System.Numerics;

namespace ASToolkit.Core.Simulation;

internal readonly record struct PlayerCommand(Vector2? Destination, Vector2 Cursor, bool IsFiring, int Weapon)
{
    public static Vector2 GetCursor(Vector3 point) => new(point.X, point.Y - point.Z);
}
