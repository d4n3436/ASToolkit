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
    private const int ArmorCount = 3;
    private const int ReservedBatches = 2;

    private readonly ObjectTable _objects;
    private readonly IReadOnlyList<int> _weaponPrices;
    private readonly IReadOnlyList<int> _ammoPrices;
    private readonly IReadOnlyList<int> _ammoBatches;
    private readonly IReadOnlyList<int> _itemPrices;

    public ShopBot(ObjectTable objects, GameTable table)
    {
        _objects = objects;
        _weaponPrices = table.GetArray("WeaponPrice");
        _ammoPrices = table.GetArray("AmmoPrice");
        _ammoBatches = table.GetArray("GettingAmmo");
        _itemPrices = table.GetArray("ItemPrice");
    }

    // Buys new weapons, ammunition to half of each weapon's capacity, the best armor, implants, then ammunition to capacity
    public PlayerLoadout Shop(ref int money, IReadOnlyDictionary<KnownObject, int> carried, PlayerImplants implants)
    {
        var ammo = carried.ToDictionary(pair => pair.Key, pair => pair.Value);
        for (int weapon = FirstWeapon; weapon < WeaponCount; weapon++)
        {
            var pickup = (KnownObject)(WeaponPickupBase + weapon);
            if (ammo.ContainsKey(pickup) || money < _weaponPrices[weapon] + ReservedBatches * _ammoPrices[weapon])
                continue;
            
            money -= _weaponPrices[weapon];
            ammo[pickup] = 0;
        }

        BuyAmmo(ref money, ammo, implants, 2);
        var armor = default(KnownObject);
        for (int item = GreenArmorItem + ArmorCount - 1; item >= GreenArmorItem; item--)
        {
            if (money < _itemPrices[item])
                continue;
            
            money -= _itemPrices[item];
            armor = KnownObject.GreenArmor + (item - GreenArmorItem);
            break;
        }

        int[] counts = [implants.Life, implants.Strength, implants.Speed, implants.Accuracy];
        int[] order = [LifeItem, AccuracyItem, StrengthItem, SpeedItem];
        for (bool bought = true; bought;)
        {
            bought = false;
            foreach (int item in order)
            {
                if (counts[item] >= PlayerImplants.MaxCount || money < _itemPrices[item])
                    continue;
                
                money -= _itemPrices[item];
                counts[item]++;
                bought = true;
            }
        }

        var upgraded = new PlayerImplants(counts[LifeItem], counts[StrengthItem], counts[SpeedItem], counts[AccuracyItem]);
        BuyAmmo(ref money, ammo, upgraded, 1);
        return new PlayerLoadout(armor, ammo, upgraded);
    }

    // Fills the best weapons first, up to the capacity divided by the given share
    private void BuyAmmo(ref int money, Dictionary<KnownObject, int> ammo, PlayerImplants implants, int divisor)
    {
        for (int weapon = WeaponCount - 1; weapon >= FirstWeapon; weapon--)
        {
            var pickup = (KnownObject)(WeaponPickupBase + weapon);
            if (!ammo.TryGetValue(pickup, out int current) || !_objects.TryFind((KnownObject)(TorsoBase + weapon), out var torso) ||
                !_objects.TryGetWeapon(torso, out var record))
            {
                continue;
            }

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
