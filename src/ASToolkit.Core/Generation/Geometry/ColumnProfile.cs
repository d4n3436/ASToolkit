using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Generation.Geometry;

/// <summary>
/// Represents the drawn rows of each screen column of a shape, relative to its top-left corner.
/// </summary>
internal readonly record struct ColumnProfile(float HalfWidth, float HalfHeight, ColumnSpan[] Columns)
{
    private const int GroundDepth = 1024;

    /// <summary>
    /// Reads the first frame of an object's sprite and splits each column at the top of the pixels no higher than a height.
    /// </summary>
    public static ColumnProfile Read(SpriteLibrary sprites, KnownObject id, int capHeight = 0)
    {
        var (_, view) = sprites.Find(id) ?? throw new InvalidDataException($"Missing sprite of object {(int)id}.");
        var columns = new ColumnSpan[view.Width];
        columns.AsSpan().Fill(new ColumnSpan(int.MaxValue, int.MaxValue, int.MinValue));
        foreach (var run in view.GetFrame(0))
        {
            for (int index = 0; index < run.Count; index++)
            {
                bool cap = view.HasDepth && run.GetDepth(index) > GroundDepth + 8 * capHeight;
                var column = columns[run.X + index];
                columns[run.X + index] = new ColumnSpan(Math.Min(column.Top, run.Y), cap ? column.Cap : Math.Min(column.Cap, run.Y), Math.Max(column.Bottom, run.Y));
            }
        }

        foreach (ref var column in columns.AsSpan())
        {
            column = column with { Cap = Math.Min(column.Cap, column.Bottom + 1) };
        }

        return new ColumnProfile(view.Width / 2.0f, view.Height / 2.0f, columns);
    }

    /// <summary>
    /// Creates the profile of a lattice cell centered on a pixel corner, grown by a margin above and below.
    /// </summary>
    public static ColumnProfile CreateDiamond(int halfWidth, int halfHeight, int margin)
    {
        var columns = new ColumnSpan[2 * halfWidth];
        for (int index = 0; index < columns.Length; index++)
        {
            float reach = halfHeight * (1 - MathF.Abs(index - halfWidth + 0.5f) / halfWidth);
            int top = (int)MathF.Ceiling(halfHeight - reach - 0.5f) - margin;
            columns[index] = new ColumnSpan(top, top, (int)MathF.Floor(halfHeight + reach - 0.5f) + margin);
        }

        return new ColumnProfile(halfWidth, halfHeight, columns);
    }

    /// <summary>
    /// Returns the profile with each row shrunk by a border at both ends and the top and bottom rows of the shape removed.
    /// </summary>
    public ColumnProfile Shrink(int border)
    {
        int top = Columns.Min(column => column.Top) + border;
        int bottom = Columns.Max(column => column.Bottom) - border;
        var columns = new ColumnSpan[Columns.Length];
        for (int index = 0; index < columns.Length; index++)
        {
            if (index < border || index >= columns.Length - border)
            {
                columns[index] = new ColumnSpan(0, 0, -1);
                continue;
            }

            var (left, middle, right) = (Columns[index - border], Columns[index], Columns[index + border]);
            int from = Math.Max(top, Math.Max(middle.Top, Math.Max(left.Top, right.Top)));
            int to = Math.Min(bottom, Math.Min(middle.Bottom, Math.Min(left.Bottom, right.Bottom)));
            columns[index] = from <= to ? new ColumnSpan(from, Math.Max(middle.Cap, from), to) : new ColumnSpan(0, 0, -1);
        }

        return this with { Columns = columns };
    }

    /// <summary>
    /// Returns the top-left corner of the shape drawn at a map position.
    /// </summary>
    public (int Left, int Top) GetOrigin(float x, float y) => ((int)MathF.Round(x - HalfWidth), (int)MathF.Round(y - HalfHeight));
}
