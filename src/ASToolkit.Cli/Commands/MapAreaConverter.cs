using System.ComponentModel;
using System.Globalization;
using System.Numerics;

namespace ASToolkit.Cli.Commands;

internal sealed class MapAreaConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is not string text)
        {
            return base.ConvertFrom(context, culture, value);
        }

        string[] parts = text.Split(',');
        if (parts.Length == 3 && float.TryParse(parts[0], CultureInfo.InvariantCulture, out float x) && float.TryParse(parts[1], CultureInfo.InvariantCulture, out float y)
            && float.TryParse(parts[2], CultureInfo.InvariantCulture, out float radius))
        {
            return new MapArea(new Vector2(x, y), radius);
        }

        throw new FormatException("Expected value in the x,y,r format (e.g,, 3200,4100,300).");
    }
}
