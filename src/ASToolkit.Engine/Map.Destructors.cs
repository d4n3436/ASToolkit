using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public sealed partial class Map
{
    /// <summary>
    /// Deletes a sprite.
    /// </summary>
    /// <param name="sprite">The sprite.</param>
    public void DeleteSprite(Sprite sprite)
    {
        sprite.Destruct(this);
    }

    [EngineRoutine(0x0043f520)]
    internal void DestructSprite(Sprite sprite)
    {
        RunDestroyHandler(sprite);
        LowerHeights(sprite.Definition, sprite);

        bool isEmpty = sprite.Definition == Definitions.Empty;

        if (isEmpty && sprite.ReferenceCount != 0)
        {
            Session.Logger.Error("SPRITE 0"u8, LogError.Invalid, 0);
        }

        if (sprite.ReferenceCount > 1)
        {
            Collector.DeleteFromHash(sprite);

            if (sprite.ReferenceCount > 1)
            {
                DeletePointerToSprite(sprite);
            }
        }

        int army = (int)sprite.Army & 3;

        if (sprite.Definition.Counts[army] != 0)
        {
            sprite.Definition.Counts[army]--;
        }

        sprite.Goal = null;

        while (sprite.Link is not null)
        {
            DeleteSprite(sprite.Link);
        }

        sprite.Owner?.Link = null;

        if (!isEmpty)
        {
            RemoveFromUpdates(sprite);
            sprite.Release();
        }

        if (sprite.ReferenceCount != 0)
        {
            Session.Logger.Error("SPRITE 0"u8, LogError.Text, 0);
        }

        _builds.Remove(sprite);

        if (sprite.Handle == 0)
            return;

        _handles.Remove(sprite.Handle);
        sprite.Handle = 0;
    }

    [EngineRoutine(0x00454660)]
    internal void DestructMan(Man man) => DestructUnit(man);

    [EngineRoutine(0x00447240)]
    internal void DestructUnit(Unit unit)
    {
        DeletePointerToSprite(unit);
        DestructSprite(unit);
    }

    [EngineRoutine(0x00448af0)]
    internal void DestructFrame(Frame sprite)
    {
        if (_menuCurrent == sprite)
        {
            _menuCurrent = null;
        }

        RemoveSprite(_menuSprites, sprite);
        DeletePointerToSprite(sprite);
        DestructSprite(sprite);
    }

    [EngineRoutine(0x0040ed10)]
    internal void DestructLinker(Linker linker)
    {
        Unlink(linker);
        DestructSprite(linker);
    }

    [EngineRoutine(0x00449700)]
    internal void DestructRegion(Region region)
    {
        DeletePointerToSprite(region);
        DestructSprite(region);
    }

    [EngineRoutine(0x00444a00)]
    private static void Unlink(Sprite sprite)
    {
        sprite.Link?.Owner = sprite.Owner;

        if (sprite.Owner is not null)
        {
            sprite.Owner.Link = sprite.Link;
            sprite.Owner = null;
        }

        sprite.Link = null;
    }
}
