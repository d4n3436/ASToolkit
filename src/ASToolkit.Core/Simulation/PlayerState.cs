using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Simulation;

internal sealed class PlayerState
{
    private const int MegaHealthBonus = 100;
    private const int ArmorIdOffset = 4;
    private const int MaxLives = 5;
    private const int LivesItem = 7;
    private static ReadOnlySpan<byte> ItemsVariable => "PlayerItem"u8;
    private static ReadOnlySpan<byte> ExplosivesVariable => "PlayerExplosive"u8;
    private readonly GameWorld _world;

    public PlayerState(GameWorld world)
    {
        _world = world;
    }

    public float Speed => _world.Player.MaxSpeed / SpriteDefinition.PerMillisecond;

    public bool CarriesDynamite => _world.ReadScriptNumber(ExplosivesVariable) > 0;

    public bool HoldsWeapon(int slot) => slot >= 0 && slot < Unit.WeaponSlots && _world.Player.Items.Contains(Unit.WeaponItemBase + slot);

    public int GetAmmo(int slot) => slot == _world.Player.WeaponSlot ? _world.Player.GetAmmo() : _world.Player.SlotAmmo[slot];

    public bool HasAmmo(int slot) => HoldsWeapon(slot) && GetAmmo(slot) > 0;

    public SpriteDefinition? GetWeapon(int slot) => slot >= 0 && slot < Unit.WeaponSlots ? _world.Definitions.Find(KnownObject.SecretWeaponTorso + slot) : null;

    public Dictionary<KnownObject, int> GetRemainingAmmo()
    {
        var held = new Dictionary<KnownObject, int>();
        for (int slot = Unit.Pistols + 1; slot < Unit.WeaponSlots; slot++)
        {
            if (HoldsWeapon(slot))
            {
                held[(KnownObject)(Unit.WeaponItemBase + slot)] = GetAmmo(slot);
            }
        }

        return held;
    }

    public bool CanTakePickup(KnownObject id)
    {
        var player = _world.Player;
        int maxHealth = player.Definition.ArmyMaxHp[(int)player.Army & 3];

        return id switch
        {
            KnownObject.Health or KnownObject.BigHealth => player.Hp < maxHealth,
            KnownObject.MegaHealth => player.Hp < maxHealth + MegaHealthBonus,
            >= KnownObject.GreenArmor and <= KnownObject.RedArmor => GetArmorHp() < (_world.Definitions.Find(id - ArmorIdOffset)?.MaxHp ?? 0),
            >= KnownObject.LifeImplant and <= KnownObject.AccuracyImplant =>
                _world.ReadScriptNumber(ItemsVariable, id - KnownObject.LifeImplant) < PlayerImplants.MaxCount,
            >= KnownObject.ShotgunAmmo and <= KnownObject.MagmaMinigunAmmo => CanTakeAmmo(id.GetAmmoSlot()),
            KnownObject.ExtraLife => _world.ReadScriptNumber(ItemsVariable, LivesItem) < MaxLives,
            KnownObject.Medkit => !player.Items.Contains((int)KnownObject.Medkit),
            KnownObject.Drone => !_world.Monsters.Any(unit => unit.Definition.Id == KnownObject.FlyingDrone && unit.IsAlive),
            KnownObject.Invulnerability or KnownObject.Dynamite or KnownObject.Money or KnownObject.Flashlight or KnownObject.NightVision or
                >= KnownObject.Shotgun and <= KnownObject.MagmaMinigun => true,
            _ => false
        };
    }

    private bool CanTakeAmmo(int slot) => HoldsWeapon(slot) && GetAmmo(slot) < (GetWeapon(slot)?.AmmoCapacity ?? 0);

    private int GetArmorHp()
    {
        for (var link = _world.Player.Link; link is not null; link = link.Link)
        {
            if (link.Definition.Id is >= KnownObject.WornGreenArmor and <= KnownObject.WornRedArmor)
                return link.Hp;
        }

        return 0;
    }
}
