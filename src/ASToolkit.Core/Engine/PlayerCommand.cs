using System.Numerics;

namespace ASToolkit.Core.Engine;

internal readonly record struct PlayerCommand(Vector2? Destination, Vector2 Cursor, bool Fires, int Weapon)
{
    public static Vector2 GetCursor(Vector3 point) => new(point.X, point.Y - point.Z);
}
