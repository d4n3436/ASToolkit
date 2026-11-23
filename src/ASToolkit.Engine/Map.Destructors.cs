using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public sealed partial class Map
{
    /// <summary>
    /// Deletes a sprite through the destructor of its class.
    /// </summary>
    /// <param name="sprite">The sprite.</param>
    public void DeleteSprite(Sprite sprite)
    {
        // TODO: Port the CivRobot (0x44a650), Engine (0x44e9d0), Rail (0x44a1a0) and Depo (0x44c0d0) destructors
        switch (sprite.Definition.Object.SpriteClass)
        {
            case SpriteClass.Man:
                DestructMan(sprite);
                break;
            case SpriteClass.Unit or SpriteClass.Building or SpriteClass.Avia or SpriteClass.CivRobot or SpriteClass.Creature or SpriteClass.Balloon:
                DestructUnit(sprite);
                break;
            case SpriteClass.Frame or SpriteClass.Text:
                DestructFrame(sprite);
                break;
            case SpriteClass.Linker:
                DestructLinker(sprite);
                break;
            case SpriteClass.Region:
                DestructRegion(sprite);
                break;
            default:
                DestructSprite(sprite);
                break;
        }
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

    [EngineRoutine(0x00454660)]
    private void DestructMan(Sprite man) => DestructUnit(man);

    [EngineRoutine(0x00447240)]
    private void DestructUnit(Sprite unit)
    {
        DeletePointerToSprite(unit);
        DestructSprite(unit);
    }

    [EngineRoutine(0x00448af0)]
    private void DestructFrame(Sprite sprite)
    {
        if (_menuCurrent == sprite)
        {
            _menuCurrent = null;
        }

        RemoveMenuSprite(sprite);
        DeletePointerToSprite(sprite);
        DestructSprite(sprite);
    }

    [EngineRoutine(0x0040ed10)]
    private void DestructLinker(Sprite linker)
    {
        Unlink(linker);
        DestructSprite(linker);
    }

    [EngineRoutine(0x00449700)]
    private void DestructRegion(Sprite region)
    {
        DeletePointerToSprite(region);
        DestructSprite(region);
    }

    [EngineRoutine(0x0043f520)]
    internal void DestructSprite(Sprite sprite)
    {
        RunDestroyHandler(sprite);
        LowerHeights(sprite.Definition, sprite);

        bool isEmpty = sprite.Definition == Definitions.Empty;

        if (isEmpty && sprite.ReferenceCount != 0)
        {
            Session.Logger.Error(SpriteLogTag, LogError.Invalid, 0, default);
        }

        if (sprite.ReferenceCount > 1)
        {
            Collector.DeleteFromHash(sprite);

            if (sprite.ReferenceCount > 1)
            {
                DeletePointerToSprite(sprite);
            }
        }

        if (sprite.IsCounted)
        {
            sprite.IsCounted = false;
            int army = (int)sprite.Army & 3;

            if (sprite.Definition.Counts[army] != 0)
            {
                sprite.Definition.Counts[army]--;
            }
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
            ForgetPlacement(sprite);
            sprite.Release();
        }

        if (sprite.ReferenceCount != 0)
        {
            Session.Logger.Error(SpriteLogTag, LogError.Text, 0, default);
        }

        if (sprite.Handle == 0)
            return;

        _handles.Remove(sprite.Handle);
        sprite.Handle = 0;
    }

    private void ForgetPlacement(Sprite sprite)
    {
        var placement = sprite.Placement;

        if (placement is null)
            return;

        if (_statics.TryGetValue(placement, out var stand) && stand == sprite)
        {
            _statics.Remove(placement);
        }

        if (_bodies.TryGetValue(placement, out var body) && body == sprite)
        {
            _bodies.Remove(placement);
        }

        if (!IsBaked(sprite.Definition))
        {
            RemoveSprite(placement);
        }
    }
}
