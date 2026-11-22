namespace ASToolkit.Core.Engine;

internal ref struct DefinitionSprites
{
    private readonly List<Sprite?> _list;
    private readonly SpriteDefinition _definition;
    private int _index;

    public DefinitionSprites(List<Sprite?> list, SpriteDefinition definition)
    {
        _list = list;
        _definition = definition;
        _index = list.Count;
        Current = null!;
    }

    public Sprite Current { get; private set; }

    public readonly DefinitionSprites GetEnumerator() => this;

    public bool MoveNext()
    {
        while (--_index >= 0)
        {
            var sprite = _list[_index];
            if (sprite is not null && sprite.Definition == _definition)
            {
                Current = sprite;
                return true;
            }
        }

        return false;
    }
}
