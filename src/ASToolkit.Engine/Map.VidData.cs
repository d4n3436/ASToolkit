namespace ASToolkit.Engine;

public partial class Map
{
    internal const int Percent = 100;

    internal void DropLinks(Sprite owner)
    {
        for (var vid = owner.Vid.LinkVid; vid is not null; vid = vid.LinkVid)
        {
            owner.DestroyLink(vid);
        }
    }

    private static Vid GetAmmoOwner(Vid vid)
    {
        var owner = vid.LinkVid;

        return owner is not null && owner.CanFight() ? owner : vid;
    }

    private protected static int Sum(ReadOnlySpan<int> values)
    {
        int total = 0;
        foreach (int value in values)
        {
            total += value;
        }

        return total;
    }
}
