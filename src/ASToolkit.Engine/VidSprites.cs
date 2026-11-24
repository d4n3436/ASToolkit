namespace ASToolkit.Engine;

internal ref struct VidSprites
{
    private readonly List<Sprite?> _list;
    private readonly Vid _vid;
    private int _index;

    public VidSprites(List<Sprite?> list, Vid vid)
    {
        _list = list;
        _vid = vid;
        _index = list.Count;
        Current = null!;
    }

    public Sprite Current { get; private set; }

    public readonly VidSprites GetEnumerator() => this;

    public bool MoveNext()
    {
        while (--_index >= 0)
        {
            var sprite = _list[_index];
            if (sprite is not null && sprite.Vid == _vid)
            {
                Current = sprite;
                return true;
            }
        }

        return false;
    }
}
