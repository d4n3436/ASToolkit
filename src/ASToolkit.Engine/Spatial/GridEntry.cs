namespace ASToolkit.Engine;

internal readonly struct GridEntry
{
    public GridEntry(Sprite sprite)
    {
        Sprite = sprite;
    }

    public Sprite Sprite { get; }
}
