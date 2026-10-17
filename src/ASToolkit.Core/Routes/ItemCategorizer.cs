using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Routes;

/// <summary>
/// Provides the category of a pickup ID.
/// </summary>
public static class ItemCategorizer
{
    /// <summary>
    /// Returns the category of a pickup ID, or null when the ID is not a pickup.
    /// </summary>
    public static ItemCategory? GetCategory(KnownObject id) => id switch
    {
        KnownObject.Health or KnownObject.BigHealth or KnownObject.MegaHealth or KnownObject.Medkit or KnownObject.ExtraLife => ItemCategory.Health,
        >= KnownObject.GreenArmor and <= KnownObject.Invulnerability => ItemCategory.Armor,
        >= KnownObject.ShotgunAmmo and <= KnownObject.MagmaMinigunAmmo => ItemCategory.Ammo,
        >= KnownObject.Shotgun and <= KnownObject.MagmaMinigun => ItemCategory.Weapon,
        KnownObject.Dynamite => ItemCategory.Dynamite,
        KnownObject.Money => ItemCategory.Money,
        >= KnownObject.LifeImplant and <= KnownObject.AccuracyImplant => ItemCategory.Implant,
        KnownObject.Drone or KnownObject.SpeedUp or KnownObject.SlowDown or KnownObject.FreezeMonsters or KnownObject.Bomb or KnownObject.SecondBomb or KnownObject.Adrenaline or KnownObject.Flashlight or KnownObject.NightVision => ItemCategory.Bonus,
        _ => null
    };
}
