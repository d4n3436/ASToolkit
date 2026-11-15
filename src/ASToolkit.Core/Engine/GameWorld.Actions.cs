using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal sealed partial class GameWorld
{
    private const int PlayerKindLimit = 20;

    [EngineRoutine(0x00454800)]
    public int Action(Unit unit, ActionCode code, int var1 = 0, int var2 = 0, int var3 = 0)
    {
        if (unit.Kind.Object.SpriteClass == SpriteClass.Man)
        {
            switch (code)
            {
                case ActionCode.CoorAttack:
                    Weapons.AimPlayer(new Vector2(var1, var2));

                    return 0;
                case ActionCode.ChangeVid:
                    ChangePlayerVid(var1, var2);

                    return 0;
                case ActionCode.AddItem:
                    AddPlayerItem(var1);

                    return 0;
            }
        }

        return UnitAction(unit, code, var1, var2, var3);
    }

    [EngineRoutine(0x00440830)]
    public int Action(Unit unit, ActionCode code, Unit target)
    {
        if (code != ActionCode.MoveTo)
            throw new NotSupportedException($"Cannot send {code} with a target.");

        Motion.SetMoveTarget(unit, target, null);

        return 0;
    }

    [EngineRoutine(0x00440830)]
    private int UnitAction(Unit unit, ActionCode code, int var1, int var2, int var3)
    {
        switch (code)
        {
            case ActionCode.Move:
                Motion.SetMoveTarget(unit, null, new Vector3(var1, var2, var3));

                return 0;
            case ActionCode.ChangeVid:
                ChangeVid(unit, var1, var2);

                return 0;
            case ActionCode.GetAmmo:
                return unit.GetAmmo(var1);
            case ActionCode.AddAmmo:
                return unit.AddAmmo(var1, var2);
            default:
                throw new NotSupportedException($"Cannot send {code}.");
        }
    }

    [EngineRoutine(0x00440830)]
    private void ChangeVid(Unit unit, int id, int state)
    {
        var kind = Kinds.Find((KnownObject)id);

        if (kind is null || kind == unit.Kind)
            return;

        if (unit != Player)
        {
            if (unit.Link?.Number < 0)
            {
                Effects.Detach(unit);
            }

            unit.Link = null;
        }

        unit.ChangeVid(kind, state < 0 ? unit.Animation : (SpriteAnimation)state);
        Units.ChangeCoor(unit);

        if (unit == Player)
        {
            Weapons.ReplaceTorso();
        }
        else
        {
            CreateLink(unit);
        }
    }

    [EngineRoutine(0x00454800)]
    private void AddPlayerItem(int item)
    {
        int slot = item - PlayerInventory.WeaponPickupBase;

        if ((uint)slot >= PlayerInventory.WeaponSlots)
            throw new NotSupportedException($"Cannot add item {item}.");

        if (!Inventory.AddWeapon(slot) && (Player.GetAmmo() == 0 || Player.WeaponSlot < slot || slot == 0))
        {
            Weapons.SelectWeapon(slot);
        }
    }

    [EngineRoutine(0x00454800)]
    private void ChangePlayerVid(int id, int state)
    {
        if ((int)Player.Kind.Id < PlayerKindLimit)
        {
            Player.SlotAmmo[Player.WeaponSlot] = Player.GetAmmo();
        }

        ChangeVid(Player, id, state);

        if ((int)Player.Kind.Id > PlayerKindLimit)
        {
            Player.AddAmmo(Player.GetAmmoCapacity() - Player.GetAmmo());
        }
        else
        {
            Player.AddAmmo(Player.SlotAmmo[Player.WeaponSlot] - Player.GetAmmo());
        }
    }
}
