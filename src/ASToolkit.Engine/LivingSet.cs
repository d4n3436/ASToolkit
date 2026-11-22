namespace ASToolkit.Engine;

internal sealed class LivingSet
{
    public List<Sprite> Sprites { get; } = [];

    public int CheckedAt { get; set; } = -1;

    public void Clear()
    {
        Sprites.Clear();
        CheckedAt = -1;
    }
}
