using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal readonly record struct SpriteFilter(SpriteSearch Search, OldSpriteType Types, SpriteSearch Armies)
{
    public bool Accepts(Sprite sprite)
    {
        var definition = sprite.Definition;

        if ((definition.Object.SpriteType.ToOldSpriteType() & Types) == 0 || (Armies & (SpriteSearch)((int)SpriteSearch.Army0 << ((int)sprite.Army & 3))) == 0)
            return false;

        if (Search.HasFlag(SpriteSearch.Free) && sprite.Command != Command.None)
            return false;

        if (Search.HasFlag(SpriteSearch.Class) && (int)definition.Object.SpriteClass != (int)(Search & SpriteSearch.ValueMask))
            return false;

        return !Search.HasFlag(SpriteSearch.Id) || (int)definition.Id == (int)(Search & SpriteSearch.ValueMask);
    }
}
