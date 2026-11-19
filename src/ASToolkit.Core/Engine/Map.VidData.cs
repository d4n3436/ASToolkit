using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

public sealed partial class Map
{
    private const int Percent = 100;
    private const int HpScale = 256;
    private const int CoefficientScale = 16;

    private static SpriteDefinition GetAmmoOwner(SpriteDefinition definition)
    {
        var owner = definition.LinkDefinition;

        return owner is not null && owner.Children[(int)SpriteAnimation.Fight] != 0 && owner.Object.WeaponIndex != 0 ? owner : definition;
    }

    [EngineRoutine(0x00414890)]
    private void SetHpCoeff(SpriteDefinition first, int army, int percent)
    {
        for (var definition = first; definition is not null; definition = definition.LinkDefinition)
        {
            int previous = definition.ArmyMaxHp[army];
            if (percent >= 0)
            {
                definition.ArmyMaxHp[army] = definition.MaxHp * percent / Percent;
            }

            if (definition.MaxHp == 0)
                continue;

            foreach (var entry in _updates.GetLayer(definition.Layer))
            {
                var sprite = entry.Sprite;
                if (sprite is not null && sprite.Definition == definition && ((int)sprite.Army & 3) == army)
                {
                    ChangeHp(sprite, sprite.Hp * definition.ArmyMaxHp[army] * CoefficientScale / previous / CoefficientScale);
                }
            }
        }
    }

    [EngineRoutine(0x00414990)]
    private void SetMaxHp(SpriteDefinition first, int army, int maxHp)
    {
        for (var definition = first; definition is not null; definition = definition.LinkDefinition)
        {
            int previous = definition.ArmyMaxHp[army];
            if (maxHp >= 0)
            {
                definition.ArmyMaxHp[army] = maxHp;
            }

            if (definition.MaxHp == 0)
                continue;

            foreach (var entry in _updates.GetLayer(definition.Layer))
            {
                var sprite = entry.Sprite;
                if (sprite is not null && sprite.Definition == definition && ((int)sprite.Army & 3) == army)
                {
                    ChangeHp(sprite, sprite.Hp * definition.ArmyMaxHp[army] * HpScale / previous / HpScale);
                }
            }
        }
    }

    [EngineRoutine(0x004454d0)]
    private void ChangeHp(Sprite sprite, int hp)
    {
        if (hp < 1)
        {
            Action(sprite, ActionCode.Damage, sprite.Hp - hp);
            return;
        }

        int half = sprite.Definition.ArmyMaxHp[(int)sprite.Army & 3] / 2;
        if (half < hp && sprite.Hp <= half)
        {
            DestroyLink(sprite, Definitions.Find((KnownObject)Math.Abs(sprite.Definition.Children[(int)SpriteAnimation.Wound])));
        }

        if (hp <= half && half < sprite.Hp)
        {
            sprite.ChangeAnimation(SpriteAnimation.Wound);
        }

        sprite.Hp = hp;
    }

    internal void DropLinks(Sprite owner)
    {
        for (var definition = owner.Definition.LinkDefinition; definition is not null; definition = definition.LinkDefinition)
        {
            DestroyLink(owner, definition);
        }
    }

    [EngineRoutine(0x00444a30)]
    private void DestroyLink(Sprite owner, SpriteDefinition? definition)
    {
        var previous = owner;
        var link = owner.Link;
        while (link is not null && link.Definition != definition)
        {
            previous = link;
            link = link.Link;
        }

        if (link is null)
            return;

        var next = link.Link;
        previous.Link = next;

        next?.Owner = previous;

        link.Link = null;
        link.Owner = null;
        DestroySprite(link);
    }

    private static int Sum(ReadOnlySpan<int> values)
    {
        int total = 0;
        foreach (int value in values)
        {
            total += value;
        }

        return total;
    }
}
