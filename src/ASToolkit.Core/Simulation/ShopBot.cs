using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Simulation;

internal sealed class ShopBot
{
    private const int FirstWeapon = 2;
    private const int WeaponCount = 10;
    private const int TorsoBase = 10;
    private const int WeaponPickupBase = 260;
    private const int LifeItem = 0;
    private const int StrengthItem = 1;
    private const int SpeedItem = 2;
    private const int AccuracyItem = 3;
    private const int GreenArmorItem = 4;
    private const int ExtraLifeItem = 7;
    private const int ArmorCount = 3;
    private const int InitialAmmoDivisor = 2;

    private readonly ObjectTable _objects;
    private readonly ObjectCatalog _kinds;
    private readonly AttackTable _attacks;
    private readonly IReadOnlyList<int> _weaponPrices;
    private readonly IReadOnlyList<int> _ammoPrices;
    private readonly IReadOnlyList<int> _ammoBatches;
    private readonly IReadOnlyList<int> _itemPrices;

    public ShopBot(ObjectTable objects, ObjectCatalog kinds, GameTable table)
    {
        _objects = objects;
        _kinds = kinds;
        _attacks = new AttackTable(kinds);
        _weaponPrices = table.GetArray("WeaponPrice");
        _ammoPrices = table.GetArray("AmmoPrice");
        _ammoBatches = table.GetArray("GettingAmmo");
        _itemPrices = table.GetArray("ItemPrice");
    }

    public PlayerLoadout Shop(ref int money, IReadOnlyDictionary<KnownObject, int> carried, PlayerImplants implants, IReadOnlyDictionary<KnownObject, int> items,
        int reward)
    {
        var bag = new Dictionary<KnownObject, int>(items);
        var ammo = new Dictionary<KnownObject, int>(carried);
        BuyAmmo(ref money, ammo, implants, InitialAmmoDivisor);
        // Skip an affordable weapon when a dearer one within the coming reward deals more damage for its price, and save for that one
        int saved = 0;
        float savedValue = 0;
        for (int slot = WeaponCount - 1; slot >= FirstWeapon; slot--)
        {
            var pickup = (KnownObject)(WeaponPickupBase + slot);

            if (ammo.ContainsKey(pickup))
                break;

            var torso = _kinds.Find(GetTorsoId(slot));
            int cost = _weaponPrices[slot] + GetHalfLoadCost(slot, implants);

            int damage = torso is null ? 0 : _attacks.Get(torso).Damage;

            if (torso is null || damage <= 0 || money + reward < cost)
                continue;

            float value = damage * GameClock.MillisecondsPerSecond /
                (float)Math.Max(GameClock.DefaultFrameTime, torso.AttackTime) / _weaponPrices[slot];

            if (money < cost)
            {
                if (value > savedValue)
                {
                    saved = cost;
                    savedValue = value;
                }

                continue;
            }

            if (value < savedValue && ammo.Count > 0)
                break;

            money -= _weaponPrices[slot];
            ammo[pickup] = 0;
            BuyAmmo(ref money, ammo, implants, InitialAmmoDivisor);
            break;
        }

        var armor = default(KnownObject);
        for (int item = GreenArmorItem + ArmorCount - 1; item >= GreenArmorItem; item--)
        {
            if (money < _itemPrices[item])
                continue;

            money -= _itemPrices[item];
            armor = KnownObject.GreenArmor + (item - GreenArmorItem);
            break;
        }

        Span<int> counts = [implants.Life, implants.Strength, implants.Speed, implants.Accuracy];
        ReadOnlySpan<int> order = [SpeedItem, LifeItem, AccuracyItem, StrengthItem];
        for (bool bought = true; bought;)
        {
            bought = false;
            foreach (int item in order)
            {
                if (counts[item] >= PlayerImplants.MaxCount || money - saved < _itemPrices[item])
                    continue;

                money -= _itemPrices[item];
                counts[item]++;
                bought = true;
            }
        }

        var upgraded = new PlayerImplants(counts[LifeItem], counts[StrengthItem], counts[SpeedItem], counts[AccuracyItem]);
        BuyLives(ref money, saved, bag, PlayerInventory.MaxExtraLives);
        money -= saved;
        BuyAmmo(ref money, ammo, upgraded, 1);
        money += saved;

        return new PlayerLoadout(armor, ammo, upgraded) { Items = bag };
    }

    private void BuyLives(ref int money, int saved, Dictionary<KnownObject, int> items, int count)
    {
        while (items.GetValueOrDefault(KnownObject.ExtraLife) < count && money - saved >= _itemPrices[ExtraLifeItem])
        {
            money -= _itemPrices[ExtraLifeItem];
            items[KnownObject.ExtraLife] = items.GetValueOrDefault(KnownObject.ExtraLife) + 1;
        }
    }

    private static KnownObject GetTorsoId(int slot) => (KnownObject)(TorsoBase + slot);

    private int GetHalfLoadCost(int weapon, PlayerImplants implants)
    {
        if (_ammoBatches[weapon] <= 0 || !_objects.TryFind(GetTorsoId(weapon), out var torso) || !_objects.TryGetWeapon(torso, out var record))
            return 0;

        int capacity = implants.ScaleAmmo(record.Ammo) / InitialAmmoDivisor;

        return (capacity + _ammoBatches[weapon] - 1) / _ammoBatches[weapon] * _ammoPrices[weapon];
    }

    // Fills the best damaging weapons first, up to the capacity divided by the given share
    private void BuyAmmo(ref int money, Dictionary<KnownObject, int> ammo, PlayerImplants implants, int divisor)
    {
        for (int weapon = WeaponCount - 1; weapon >= FirstWeapon; weapon--)
        {
            var pickup = (KnownObject)(WeaponPickupBase + weapon);

            if (!ammo.TryGetValue(pickup, out int current) || !_objects.TryFind(GetTorsoId(weapon), out var torso) ||
                !_objects.TryGetWeapon(torso, out var record))
            {
                continue;
            }

            var kind = _kinds.Find(GetTorsoId(weapon));

            if (kind is null || _attacks.Get(kind).Damage <= 0)
                continue;

            int capacity = implants.ScaleAmmo(record.Ammo);
            while (current < capacity / divisor && money >= _ammoPrices[weapon] && _ammoBatches[weapon] > 0)
            {
                money -= _ammoPrices[weapon];
                current = Math.Min(capacity, current + _ammoBatches[weapon]);
            }

            ammo[pickup] = current;
        }
    }
}
