using ASToolkit.Engine.IO;

namespace ASToolkit.Engine;

internal sealed class Relation
{
    private readonly MyList<int> _handles = new();
    private readonly MyList<Sprite?> _sprites = new();

    [EngineRoutine(0x00412b10)]
    public Sprite? Decode(int handle)
    {
        for (int i = _handles.Count - 1; i >= 0; i--)
        {
            if (_handles[i] == handle)
                return _sprites[i];
        }

        return null;
    }

    [EngineRoutine(0x00412b50)]
    public void Insert(int handle, Sprite? sprite)
    {
        if (handle == 0)
            return;

        _handles.Insert(handle);
        _sprites.Insert(sprite);
    }

    [EngineRoutine(0x00412c60)]
    public void Release()
    {
        _handles.Release();
        _sprites.Release();
    }
}
