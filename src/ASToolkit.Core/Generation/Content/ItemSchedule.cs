using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Generation.Content;

/// <summary>
/// Provides the first campaign level each pickup may appear on.
/// </summary>
public static class ItemSchedule
{
    private static readonly Dictionary<KnownObject, int> FirstLevel = new()
    {
        [KnownObject.Shotgun] = 2, [KnownObject.GrenadeLauncher] = 3, [KnownObject.Minigun] = 3, [KnownObject.RocketLauncher] = 4, [KnownObject.FreezeRifle] = 5, [KnownObject.PlasmaRifle] = 6, [KnownObject.FlameThrower] = 7, [KnownObject.MagmaMinigun] = 9,
        [KnownObject.ShotgunAmmo] = 2, [KnownObject.GrenadeLauncherAmmo] = 3, [KnownObject.MinigunAmmo] = 3, [KnownObject.RocketLauncherAmmo] = 4, [KnownObject.FreezeRifleAmmo] = 5, [KnownObject.PlasmaRifleAmmo] = 6, [KnownObject.FlameThrowerAmmo] = 7, [KnownObject.MagmaMinigunAmmo] = 9,
        [KnownObject.Dynamite] = 2, [KnownObject.Money] = 2, [KnownObject.Health] = 2, [KnownObject.BigHealth] = 2, [KnownObject.MegaHealth] = 4, [KnownObject.Medkit] = 3, [KnownObject.GreenArmor] = 2, [KnownObject.YellowArmor] = 4, [KnownObject.RedArmor] = 7,
        [KnownObject.ExtraLife] = 3, [KnownObject.Flashlight] = 2, [KnownObject.SpeedImplant] = 4, [KnownObject.LifeImplant] = 5, [KnownObject.StrengthImplant] = 5, [KnownObject.Invulnerability] = 5, [KnownObject.AccuracyImplant] = 6, [KnownObject.Drone] = 6
    };

    private static readonly KnownObject[] Perks = [KnownObject.GreenArmor, KnownObject.YellowArmor, KnownObject.RedArmor, KnownObject.Invulnerability, KnownObject.Medkit, KnownObject.Drone, KnownObject.ExtraLife, KnownObject.LifeImplant, KnownObject.StrengthImplant, KnownObject.SpeedImplant, KnownObject.AccuracyImplant];

    /// <summary>
    /// Returns the ammunition of a weapon.
    /// </summary>
    public static KnownObject GetAmmo(KnownObject weapon) => weapon + 40;

    /// <summary>
    /// Returns whether a pickup is a weapon.
    /// </summary>
    public static bool IsWeapon(KnownObject id) => id is >= KnownObject.Shotgun and <= KnownObject.MagmaMinigun;

    /// <summary>
    /// Returns whether a pickup is ammunition.
    /// </summary>
    public static bool IsAmmo(KnownObject id) => id is >= KnownObject.ShotgunAmmo and <= KnownObject.MagmaMinigunAmmo;

    /// <summary>
    /// Returns whether a pickup may lie on a level; secret closets may hold it a number of levels early.
    /// </summary>
    public static bool IsAllowed(KnownObject item, int level, int levelsEarly = 0) => FirstLevel.TryGetValue(item, out int first) && first <= level + levelsEarly;

    /// <summary>
    /// Returns the weapons allowed on a level.
    /// </summary>
    public static KnownObject[] GetWeapons(int level, int levelsEarly = 0)
        => Enumerable.Range((int)KnownObject.Shotgun, KnownObject.MagmaMinigun - KnownObject.Shotgun + 1) 
            .Cast<KnownObject>()
            .Where(w => IsAllowed(w, level, levelsEarly))
            .ToArray();

    /// <summary>
    /// Returns the pickup itself when it is allowed on the level, otherwise an allowed pickup of the same kind or money.
    /// </summary>
    public static KnownObject Replace(KnownObject item, int level, Random rng)
    {
        if (IsAllowed(item, level))
        {
            return item;
        }

        if (IsWeapon(item) || IsAmmo(item))
        {
            var weapons = GetWeapons(level);
            return GetAmmo(weapons[rng.Next(weapons.Length)]);
        }

        if (Perks.Contains(item) || item == KnownObject.MegaHealth)
        {
            var allowed = new[] { KnownObject.BigHealth, KnownObject.Health, KnownObject.GreenArmor, KnownObject.YellowArmor, KnownObject.ExtraLife }
                .Where(i => IsAllowed(i, level))
                .ToArray();
            
            return allowed[rng.Next(allowed.Length)];
        }

        return KnownObject.Money;
    }

    /// <summary>
    /// Returns the contents of a secret closet, in the shares the originals use. A closet holds a money pile, an ammunition
    /// cache, a weapon taken from the level's weapons with some of its ammunition, or a single perk. When no weapon is left
    /// the closet holds one of the others.
    /// </summary>
    public static KnownObject[] CreateStash(int level, Random rng, Func<KnownObject?> takeWeapon)
    {
        float roll = rng.NextSingle();
        if (roll is >= 0.63f and < 0.89f)
        {
            var weapon = takeWeapon();
            if (weapon is not null)
            {
                return Enumerable.Repeat(GetAmmo(weapon.Value), rng.Next(0, 5))
                    .Prepend(weapon.Value)
                    .ToArray();
            }

            roll = rng.NextSingle() * 0.74f;
            if (roll >= 0.63f)
            {
                roll = 0.89f;
            }
        }

        if (roll < 0.42f)
        {
            return Enumerable.Repeat(KnownObject.Money, rng.Next(3, 13)).ToArray();
        }

        if (roll < 0.63f)
        {
            var weapons = GetWeapons(level, 1);
            int kinds = rng.Next(1, 3);
            var chosen = Enumerable.Range(0, kinds).Select(_ => GetAmmo(weapons[rng.Next(weapons.Length)])).ToArray();
            return Enumerable.Range(0, rng.Next(2, 5)).Select(i => chosen[i % chosen.Length]).ToArray();
        }

        var perks = Perks.Where(p => IsAllowed(p, level, 1)).ToArray();
        return Enumerable.Repeat(KnownObject.Money, rng.Next(0, 4)).Prepend(perks[rng.Next(perks.Length)]).ToArray();
    }
}
