using ASToolkit.Engine;
using ASToolkit.Formats;
using ASToolkit.Analysis.Levels;

namespace ASToolkit.Bot;

internal sealed class PlayerState
{
    private const int MegaHealthBonus = 100;
    private const int ArmorIdOffset = 4;
    private const int MaxLives = 5;
    private const int LivesItem = 7;

    private readonly Map _map;

    public PlayerState(Map map)
    {
        _map = map;
    }

    public float Speed => _map.Player.MaxSpeed / Vid.PerMillisecond;

    public bool HasDynamite => _map.ReadScriptNumber(ExplosivesVariable) > 0;

    private static ReadOnlySpan<byte> ItemsVariable => "PlayerItem"u8;

    private static ReadOnlySpan<byte> ExplosivesVariable => "PlayerExplosive"u8;

    public bool HoldsWeapon(int slot) => slot is >= 0 and < Sprite.WeaponSlots && _map.Player.Items.Contains(Sprite.WeaponItemBase + slot);

    public int GetAmmo(int slot) => slot == _map.Player.WeaponSlot ? ((Man)_map.Player).GetAmmo() : ((Man)_map.Player).SlotAmmo[slot];

    public bool HasAmmo(int slot) => HoldsWeapon(slot) && GetAmmo(slot) > 0;

    public Vid? GetWeapon(int slot) => slot is >= 0 and < Sprite.WeaponSlots ? _map.FindVid(KnownObject.SecretWeaponTorso + slot) : null;

    public Dictionary<KnownObject, int> GetRemainingAmmo()
    {
        var held = new Dictionary<KnownObject, int>();
        for (int slot = Sprite.Pistols + 1; slot < Sprite.WeaponSlots; slot++)
        {
            if (HoldsWeapon(slot))
            {
                held[(KnownObject)(Sprite.WeaponItemBase + slot)] = GetAmmo(slot);
            }
        }

        return held;
    }

    public bool CanTakePickup(KnownObject id)
    {
        var player = _map.Player;
        int maxHealth = player.Vid.ArmyMaxHp[(int)player.Army & 3];

        return id switch
        {
            KnownObject.Health or KnownObject.BigHealth => player.Hp < maxHealth,
            KnownObject.MegaHealth => player.Hp < maxHealth + MegaHealthBonus,
            >= KnownObject.GreenArmor and <= KnownObject.RedArmor => GetArmorHp() < (_map.FindVid(id - ArmorIdOffset)?.MaxHp ?? 0),
            >= KnownObject.LifeImplant and <= KnownObject.AccuracyImplant =>
                _map.ReadScriptNumber(ItemsVariable, id - KnownObject.LifeImplant) < PlayerImplants.MaxCount,
            >= KnownObject.ShotgunAmmo and <= KnownObject.MagmaMinigunAmmo => CanTakeAmmo(id.GetAmmoSlot()),
            KnownObject.ExtraLife => _map.ReadScriptNumber(ItemsVariable, LivesItem) < MaxLives,
            KnownObject.Medkit => !player.Items.Contains((int)KnownObject.Medkit),
            KnownObject.Drone => !_map.HasLivingMonster(KnownObject.FlyingDrone),
            KnownObject.Invulnerability or KnownObject.Dynamite or KnownObject.Money or KnownObject.Flashlight or KnownObject.NightVision or
                >= KnownObject.Shotgun and <= KnownObject.MagmaMinigun => true,
            _ => false
        };
    }

    private bool CanTakeAmmo(int slot) => HoldsWeapon(slot) && GetAmmo(slot) < (GetWeapon(slot)?.AmmoCapacity ?? 0);

    private int GetArmorHp()
    {
        foreach (var link in _map.Player.EnumerateLinks())
        {
            if (link.Vid.Id is >= KnownObject.WornGreenArmor and <= KnownObject.WornRedArmor)
                return link.Hp;
        }

        return 0;
    }
}
