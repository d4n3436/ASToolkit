using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal sealed class PlayerInventory
{
    public const int WeaponSlots = 10;
    public const int Pistols = 1;
    public const int WeaponPickupBase = 260;
    private const int FirstStrengthenedSlot = 2;
    private const int AmmoPickupBase = 300;
    private const int ArmorObjectOffset = 4;
    public const int MaxExtraLives = 5;
    private const int SmallHealthAmount = 20;
    private const int BigHealthAmount = 50;
    private const int MegaHealthBonus = 100;
    private const int MoneyPickupAmount = 50;
    private const int PercentScale = 100;
    private const int ArmorRounding = PercentScale / 2;
    private static readonly int[] ArmorShares = [50, 70, 90];
    private readonly ObjectCatalog _kinds;
    private readonly IReadOnlyList<int> _ammoBatches;
    private readonly Unit _player;
    private readonly Dictionary<KnownObject, int> _items;
    private readonly ObjectDefinition?[] _torsos = new ObjectDefinition?[WeaponSlots];
    private readonly int[] _ammo = new int[WeaponSlots];
    private readonly bool[] _held = new bool[WeaponSlots];
    private PlayerImplants _implants;
    private int _maxHealth;
    private int _armorHp;
    private int _armorShare;
    private KnownObject _stowedArmor;

    public PlayerInventory(ObjectCatalog kinds, IReadOnlyList<int> ammoBatches, Unit player, PlayerLoadout loadout)
    {
        _kinds = kinds;
        _ammoBatches = ammoBatches;
        _player = player;
        _items = loadout.Items.ToDictionary();
        _implants = loadout.Implants;
        _maxHealth = _implants.ScaleHealth(player.Kind.MaxHp);
        Speed = _implants.ScaleSpeed(player.Kind.Object.Speed);
        for (int slot = 0; slot < WeaponSlots; slot++)
        {
            _torsos[slot] = kinds.Find(KnownObject.SecretWeaponTorso + slot);
        }

        _held[Pistols] = true;
        _ammo[Pistols] = ObjectDefinition.Unlimited;
        foreach ((var weapon, int ammo) in loadout.Ammo)
        {
            int slot = (int)weapon - WeaponPickupBase;
            _held[slot] = true;
            _ammo[slot] = ammo;
        }

        if (loadout.Armor != 0)
        {
            WearArmor(loadout.Armor);
        }
    }

    public PlayerImplants Implants => _implants;

    public float Speed { get; private set; }

    public void SetSpeedBoost(float boost) => Speed = _implants.ScaleSpeed(_player.Kind.Object.Speed) + boost;

    public void SetItem(KnownObject id, int count) => _items[id] = count;

    public int Money { get; private set; }

    public int Armor => Math.Max(0, _armorHp);
    public IReadOnlyDictionary<KnownObject, int> Items => _items;

    public bool TryConsumeItem(KnownObject item)
    {
        int count = _items.GetValueOrDefault(item);

        if (count == 0)
            return false;

        if (count == 1)
        {
            _items.Remove(item);
        }
        else
        {
            _items[item] = count - 1;
        }

        return true;
    }

    public bool TryRescue()
    {
        if (!TryConsumeItem(KnownObject.Medkit))
            return false;

        _player.Hp = _maxHealth;

        return true;
    }

    public bool TryRespawn()
    {
        if (!TryConsumeItem(KnownObject.ExtraLife))
            return false;

        _player.Hp = _maxHealth;
        _armorHp = 0;
        _armorShare = 0;

        return true;
    }

    public int AbsorbDamage(int damage)
    {
        if (_armorHp > 0)
        {
            _armorHp -= (_armorShare * damage + ArmorRounding) / PercentScale;
            damage -= _armorShare * damage / PercentScale;
        }

        return damage;
    }

    public bool CanTakePickup(KnownObject id) => id switch
    {
        KnownObject.Health or KnownObject.BigHealth => _player.Hp < _maxHealth,
        KnownObject.MegaHealth => _player.Hp < _maxHealth + MegaHealthBonus,
        >= KnownObject.GreenArmor and <= KnownObject.RedArmor => _armorHp < (_kinds.Find(id - ArmorObjectOffset)?.MaxHp ?? 0),
        >= KnownObject.LifeImplant and <= KnownObject.AccuracyImplant => IsWithinCap(GetImplanted(id)),
        >= KnownObject.ShotgunAmmo and <= KnownObject.MagmaMinigunAmmo => _ammo[(int)id - AmmoPickupBase] < GetCapacity((int)id - AmmoPickupBase),
        KnownObject.Invulnerability or KnownObject.Dynamite or KnownObject.Money or >= KnownObject.Shotgun and <= KnownObject.MagmaMinigun => true,
        KnownObject.ExtraLife => _items.GetValueOrDefault(id) < MaxExtraLives,
        KnownObject.Medkit or KnownObject.Drone => _items.GetValueOrDefault(id) == 0,
        KnownObject.Flashlight or KnownObject.NightVision => true,
        _ => false
    };

    public bool TryTakePickup(KnownObject id)
    {
        if (!CanTakePickup(id))
            return false;

        switch (id)
        {
            case KnownObject.Health or KnownObject.BigHealth or KnownObject.MegaHealth:
                int limit = id == KnownObject.MegaHealth ? _maxHealth + MegaHealthBonus : _maxHealth;
                _player.Hp = Math.Min(limit, _player.Hp + id switch { KnownObject.Health => SmallHealthAmount, KnownObject.BigHealth => BigHealthAmount, _ => MegaHealthBonus });
                break;
            case >= KnownObject.GreenArmor and <= KnownObject.RedArmor:
                WearArmor(id);
                break;
            case >= KnownObject.LifeImplant and <= KnownObject.AccuracyImplant:
                _implants = GetImplanted(id);
                _maxHealth = _implants.ScaleHealth(_player.Kind.MaxHp);
                Speed = _implants.ScaleSpeed(_player.Kind.Object.Speed);
                break;
            case KnownObject.Money:
                Money += MoneyPickupAmount;
                break;
            case KnownObject.Invulnerability:
                break;
            case KnownObject.Dynamite or KnownObject.ExtraLife:
                _items[id] = _items.GetValueOrDefault(id) + 1;
                break;
            case KnownObject.Medkit or KnownObject.Drone or KnownObject.Flashlight or KnownObject.NightVision:
                _items[id] = 1;
                break;
            case >= KnownObject.Shotgun and <= KnownObject.MagmaMinigun:
                _held[(int)id - WeaponPickupBase] = true;
                AddAmmo((int)id - WeaponPickupBase, _ammoBatches[(int)id - WeaponPickupBase] / 2);
                break;
            default:
                AddAmmo((int)id - AmmoPickupBase, _ammoBatches[(int)id - AmmoPickupBase]);
                break;
        }

        return true;
    }

    private PlayerImplants GetImplanted(KnownObject id) => id switch
    {
        KnownObject.LifeImplant => _implants with { Life = _implants.Life + 1 },
        KnownObject.StrengthImplant => _implants with { Strength = _implants.Strength + 1 },
        KnownObject.SpeedImplant => _implants with { Speed = _implants.Speed + 1 },
        _ => _implants with { Accuracy = _implants.Accuracy + 1 }
    };

    private static bool IsWithinCap(PlayerImplants implants) =>
        Math.Max(Math.Max(implants.Life, implants.Strength), Math.Max(implants.Speed, implants.Accuracy)) <= PlayerImplants.MaxCount;

    [ScriptFunction("F243_0")]
    private int GetCapacity(int weapon) =>
        _torsos[weapon] is null ? 0 : weapon < FirstStrengthenedSlot ? _torsos[weapon]!.AmmoCapacity : _implants.ScaleAmmo(_torsos[weapon]!.AmmoCapacity);

    private void AddAmmo(int weapon, int amount) => _ammo[weapon] = Math.Min(_torsos[weapon] is null ? amount : GetCapacity(weapon), _ammo[weapon] + amount);
    
    public void StowArmor()
    {
        _stowedArmor = _armorHp > 0 ? KnownObject.GreenArmor + Array.IndexOf(ArmorShares, _armorShare) : 0;
        _armorHp = 0;
        _armorShare = 0;
    }
    
    public void RestoreArmor()
    {
        if (_stowedArmor != 0)
        {
            WearArmor(_stowedArmor);
        }

        _stowedArmor = 0;
    }

    private void WearArmor(KnownObject pickup)
    {
        _armorHp = _kinds.Find(pickup - ArmorObjectOffset)?.MaxHp ?? 0;
        _armorShare = ArmorShares[pickup - KnownObject.GreenArmor];
    }

    public Dictionary<KnownObject, int> GetRemainingAmmo()
    {
        var held = new Dictionary<KnownObject, int>();
        for (int weapon = Pistols + 1; weapon < WeaponSlots; weapon++)
        {
            if (_held[weapon])
            {
                held[(KnownObject)(WeaponPickupBase + weapon)] = _ammo[weapon];
            }
        }

        return held;
    }

    public ObjectDefinition? GetWeapon(int slot) => (uint)slot < WeaponSlots ? _torsos[slot] : null;

    public bool HoldsWeapon(int slot) => (uint)slot < WeaponSlots && _held[slot];

    public static int GetAmmoSlot(KnownObject id) => id is >= KnownObject.ShotgunAmmo and <= KnownObject.MagmaMinigunAmmo ? (int)id - AmmoPickupBase : -1;

    public bool TrySpendAmmo(int slot)
    {
        if (!HasAmmo(slot))
            return false;

        if (_ammo[slot] < ObjectDefinition.Unlimited)
        {
            _ammo[slot]--;
        }

        return true;
    }

    public bool HasAmmo(int slot) => (uint)slot < WeaponSlots && _held[slot] && _ammo[slot] > 0;
}
