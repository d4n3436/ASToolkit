using ASToolkit.Core.Engine.Scripts;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

public sealed partial class GameWorld
{
    private const int Percent = 100;
    private const int HpScale = 256;

    [EngineRoutine(0x00437ef2)]
    private void GetVidData(Script script)
    {
        int type = script.PopNumber();
        var definition = Definitions.Find((KnownObject)script.PopNumber());
        if (definition is null)
        {
            script.PushNumber(0);
            return;
        }

        switch ((VidDataType)type)
        {
            case VidDataType.MaxHp:
                script.PushNumber(definition.MaxHp);
                break;
            case VidDataType.BattleRange:
                script.PushNumber((int)definition.BattleRange);
                break;
            case VidDataType.Ammo:
                script.PushNumber(GetAmmoOwner(definition).AmmoCapacity);
                break;
            case VidDataType.Count:
                script.PushNumber(Sum(definition.Counts));
                break;
            case VidDataType.KilledUnit:
                script.PushNumber(Sum(definition.Killed));
                break;
            case >= VidDataType.KilledUnit0 and <= VidDataType.KilledUnit3:
                script.PushNumber(definition.Killed[type - (int)VidDataType.KilledUnit0]);
                break;
            case >= VidDataType.Count0 and <= VidDataType.Count3:
                script.PushNumber(definition.Counts[type - (int)VidDataType.Count0]);
                break;
            case >= VidDataType.MaxHp0 and <= VidDataType.MaxHp3:
                script.PushNumber(definition.ArmyMaxHp[type - (int)VidDataType.MaxHp0]);
                break;
            case VidDataType.SpriteClass:
                script.PushNumber((int)definition.Object.SpriteClass);
                break;
            case VidDataType.Speed:
                script.PushNumber(definition.ScriptSpeed);
                break;
            case VidDataType.Lifetime:
                script.PushNumber(definition.Lifetime);
                break;
            case VidDataType.DetectRange:
                script.PushNumber((int)definition.DetectRange);
                break;
            case VidDataType.WeaponAim:
                script.PushNumber((int)definition.Aim);
                break;
            case VidDataType.MoveMask:
                script.PushNumber((int)definition.MoveMask);
                break;
            case VidDataType.BuildTime:
                script.PushNumber(definition.BuildTime);
                break;
            case VidDataType.NotCreateAsChild:
                script.PushNumber(definition.NotCreatedAsChild);
                break;
            case VidDataType.Link:
                script.PushNumber((int)(definition.LinkDefinition?.Id ?? 0));
                break;
            case >= VidDataType.Child and < VidDataType.ChildCounts:
                script.PushNumber((int)(Definitions.Find((KnownObject)Math.Abs(definition.Children[type - (int)VidDataType.Child]))?.Id ?? 0));
                break;
            case >= VidDataType.ChildCounts and < VidDataType.Damage:
                script.PushNumber(definition.ChildCounts[type - (int)VidDataType.ChildCounts]);
                break;
            case VidDataType.Damage:
                script.PushNumber(definition.Damage);
                break;
            case VidDataType.RecolorUnit:
                script.PushNumber(Sum(definition.Recolored));
                break;
            case >= VidDataType.RecolorUnit0 and <= VidDataType.RecolorUnit3:
                script.PushNumber(definition.Recolored[type - (int)VidDataType.RecolorUnit0]);
                break;
            case VidDataType.Name:
                script.PushText(GameTextEncoding.Cyrillic.GetBytes(Definitions.GetName(definition)));
                break;
            case VidDataType.SpriteType:
                script.PushNumber((int)definition.Object.SpriteType.ToOldSpriteType());
                break;
            case VidDataType.Directions:
                script.PushNumber(definition.Object.Directions);
                break;
            case VidDataType.Hide:
                script.PushNumber(definition.IsHidden ? 1 : 0);
                break;
            case VidDataType.FrameSpeed:
                script.PushNumber((ushort)definition.SpriteSpeed);
                break;
        }
    }

    [EngineRoutine(0x00437ef2)]
    private void SetVidData(Script script)
    {
        int value = script.PopNumber();
        int type = script.PopNumber();
        var definition = Definitions.Find((KnownObject)script.PopNumber());
        if (definition is null)
            return;

        switch ((VidDataType)type)
        {
            case VidDataType.MaxHp:
                definition.MaxHp = value;
                break;
            // TODO: Port the gamma
            case >= VidDataType.Gamma and < VidDataType.Properties:
                break;
            case VidDataType.Ammo:
                GetAmmoOwner(definition).AmmoCapacity = value;
                break;
            case VidDataType.KilledUnit:
                definition.Killed.Fill(value);
                break;
            case >= VidDataType.KilledUnit0 and <= VidDataType.KilledUnit3:
                definition.Killed[type - (int)VidDataType.KilledUnit0] = value;
                break;
            case >= VidDataType.MaxHp0 and <= VidDataType.MaxHp3:
                SetArmyMaxHp(definition, type - (int)VidDataType.MaxHp0, value, false);
                break;
            case >= VidDataType.HpCoefficient0 and <= VidDataType.HpCoefficient3:
                SetArmyMaxHp(definition, type - (int)VidDataType.HpCoefficient0, value, true);
                break;
            case VidDataType.Speed:
                definition.Speed = value == SpriteDefinition.Unlimited ? SpriteDefinition.Unlimited : value * SpriteDefinition.PerMillisecond;
                foreach (var entry in _updates.GetLayer(definition.Layer))
                {
                    if (entry.Unit is not null && entry.Unit.Definition == definition)
                    {
                        entry.Unit.MaxSpeed = definition.Speed;
                    }
                }

                break;
            case VidDataType.Lifetime:
                if (definition.Id != 0)
                {
                    definition.Lifetime = value;
                    definition.HasKeyframes = true;
                }

                break;
            case VidDataType.DetectRange:
                definition.DetectRange = value;
                break;
            case VidDataType.WeaponAim:
                definition.Aim = value;
                break;
            case VidDataType.ExchangeVid:
            {
                var other = Definitions.Find((KnownObject)value);
                if (other is not null)
                {
                    Definitions.Exchange(definition, other);
                }

                break;
            }
            case VidDataType.MoveMask:
                definition.MoveMask = (CollisionLayers)value;
                break;
            case VidDataType.BuildTime:
                definition.BuildTime = value;
                break;
            case VidDataType.NotCreateAsChild:
                definition.NotCreatedAsChild = value;
                break;
            case VidDataType.Link:
                definition.LinkDefinition = Definitions.Find((KnownObject)value);
                break;
            case >= VidDataType.Child and < VidDataType.ChildCounts:
            {
                var child = Definitions.Find((KnownObject)Math.Abs(value));
                if (value == 0 || child is not null)
                {
                    definition.Children[type - (int)VidDataType.Child] = value;
                }

                if (child is not null && child.Object.Properties.HasFlag(ObjectProperties.BirthAsSmoke))
                {
                    definition.HasKeyframes = true;
                }

                break;
            }
            case >= VidDataType.ChildCounts and < VidDataType.Damage:
                definition.ChildCounts[type - (int)VidDataType.ChildCounts] = value;
                break;
            case VidDataType.Damage:
                definition.Damage = value;
                break;
            case VidDataType.Hide:
                for (var hidden = definition; hidden is not null; hidden = hidden.LinkDefinition)
                {
                    hidden.IsHidden = value != 0;
                }

                break;
            case VidDataType.FrameSpeed:
                definition.SetFrameSpeed((ushort)value);
                break;
        }
    }

    [EngineRoutine(0x0043a200)]
    private static SpriteDefinition GetAmmoOwner(SpriteDefinition definition)
    {
        var owner = definition.LinkDefinition;

        return owner is not null && owner.Children[(int)SpriteAnimation.Fight] != 0 && owner.Object.WeaponIndex != 0 ? owner : definition;
    }

    [EngineRoutine(0x00414990)]
    [EngineRoutine(0x00414890)]
    private void SetArmyMaxHp(SpriteDefinition first, int army, int value, bool isPercent)
    {
        for (var definition = first; definition is not null; definition = definition.LinkDefinition)
        {
            int max = isPercent ? definition.MaxHp * value / Percent : value;
            int previous = definition.ArmyMaxHp[army];
            if (max >= 0)
            {
                definition.ArmyMaxHp[army] = max;
            }

            if (definition.MaxHp == 0)
                continue;

            foreach (var entry in _updates.GetLayer(definition.Layer))
            {
                var unit = entry.Unit;
                if (unit is not null && unit.Definition == definition && ((int)unit.Army & 3) == army)
                {
                    SetUnitHp(unit, unit.Hp * definition.ArmyMaxHp[army] * HpScale / previous / HpScale);
                }
            }
        }
    }

    [EngineRoutine(0x004454d0)]
    private void SetUnitHp(Unit unit, int hp)
    {
        if (hp < 1)
        {
            Damage(unit, unit.Hp - hp);
            return;
        }

        int half = unit.Definition.ArmyMaxHp[(int)unit.Army & 3] / 2;
        if (half < hp && unit.Hp <= half)
        {
            DropLink(unit, Definitions.Find((KnownObject)Math.Abs(unit.Definition.Children[(int)SpriteAnimation.Wound])));
        }

        if (hp <= half && half < unit.Hp)
        {
            unit.ChangeAnimation(SpriteAnimation.Wound);
        }

        unit.Hp = hp;
    }

    [EngineRoutine(0x00445770)]
    [EngineRoutine(0x00440830)]
    internal void DropLinks(Unit owner)
    {
        for (var definition = owner.Definition.LinkDefinition; definition is not null; definition = definition.LinkDefinition)
        {
            DropLink(owner, definition);
        }
    }

    [EngineRoutine(0x00444a30)]
    private void DropLink(Unit owner, SpriteDefinition? definition)
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

        if (next is not null)
        {
            next.Owner = previous;
            next.OwnerOffset = next.Position - previous.Position;
        }

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
