using System.Globalization;
using System.Numerics;

namespace ASToolkit.Cli.Commands;

internal readonly record struct MapArea(Vector2 Center, float Radius)
{
    public bool Contains(Vector2 point) => Vector2.Distance(Center, point) <= Radius;

    public static bool TryParse(string text, out MapArea area)
    {
        string[] parts = text.Split(',');
        area = default;
        if (parts.Length != 3 || !float.TryParse(parts[0], CultureInfo.InvariantCulture, out float x) || !float.TryParse(parts[1], CultureInfo.InvariantCulture, out float y)
            || !float.TryParse(parts[2], CultureInfo.InvariantCulture, out float radius))
        {
            return false;
        }

        area = new MapArea(new Vector2(x, y), radius);
        return true;
    }
}
