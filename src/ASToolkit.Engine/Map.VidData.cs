using ASToolkit.Formats;

namespace ASToolkit.Engine;

public sealed partial class Map
{
    internal const int Percent = 100;
    private const int HpScale = 256;
    private const int CoefficientScale = 16;

    internal void DropLinks(Sprite owner)
    {
        for (var vid = owner.Vid.LinkVid; vid is not null; vid = vid.LinkVid)
        {
            DestroyLink(owner, vid);
        }
    }

    [EngineRoutine(0x004454d0)]
    internal void ChangeHp(Sprite sprite, int hp)
    {
        if (hp < 1 && sprite.Vid.MaxHp != 0)
        {
            if (sprite.Animation < SpriteAnimation.Death)
            {
                int army = (int)sprite.Army & 3;
                sprite.Vid.Killed[army]++;
                sprite.ChangeAnimation(sprite.Vid.ArmyMaxHp[army] < sprite.Hp - hp && Vids.Find((KnownObject)Math.Abs(sprite.Vid.Children[(int)SpriteAnimation.Death2])) is not null ? SpriteAnimation.Death2 : SpriteAnimation.Death);
            }

            return;
        }

        int half = sprite.Vid.ArmyMaxHp[(int)sprite.Army & 3] / 2;
        if (half < hp && sprite.Hp <= half)
        {
            DestroyLink(sprite, Vids.Find((KnownObject)Math.Abs(sprite.Vid.Children[(int)SpriteAnimation.Wound])));
        }

        if (hp <= half && half < sprite.Hp)
        {
            sprite.ChangeAnimation(SpriteAnimation.Wound);
        }

        sprite.Hp = hp;
    }

    [EngineRoutine(0x00444a30)]
    internal void DestroyLink(Sprite owner, Vid? vid)
    {
        var previous = owner;
        var link = owner.Link;
        while (link is not null && link.Vid != vid)
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
        DeleteSprite(link);
    }

    private static Vid GetAmmoOwner(Vid vid)
    {
        var owner = vid.LinkVid;

        return owner is not null && owner.CanFight() ? owner : vid;
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

    [EngineRoutine(0x00414890)]
    private void SetHpCoeff(Vid first, int army, int percent)
    {
        army &= 3;
        for (var vid = first; vid is not null; vid = vid.LinkVid)
        {
            int previous = vid.ArmyMaxHp[army];
            if (percent >= 0)
            {
                vid.ArmyMaxHp[army] = vid.MaxHp * percent / Percent;
            }

            if (vid.MaxHp == 0)
                continue;

            var list = _updates.GetLayer(vid.Layer);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var sprite = list[i];
                if (sprite is not null && sprite.Vid == vid && ((int)sprite.Army & 3) == army)
                {
                    ChangeHp(sprite, sprite.Hp * vid.ArmyMaxHp[army] * CoefficientScale / previous / CoefficientScale);
                }
            }
        }
    }

    [EngineRoutine(0x00414990)]
    private void SetMaxHp(Vid first, int army, int maxHp)
    {
        army &= 3;
        for (var vid = first; vid is not null; vid = vid.LinkVid)
        {
            int previous = vid.ArmyMaxHp[army];
            if (maxHp >= 0)
            {
                vid.ArmyMaxHp[army] = maxHp;
            }

            if (vid.MaxHp == 0)
                continue;

            var list = _updates.GetLayer(vid.Layer);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var sprite = list[i];
                if (sprite is not null && sprite.Vid == vid && ((int)sprite.Army & 3) == army)
                {
                    ChangeHp(sprite, sprite.Hp * vid.ArmyMaxHp[army] * HpScale / previous / HpScale);
                }
            }
        }
    }

}
