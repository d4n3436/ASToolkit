using System.Collections.ObjectModel;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the equipment the player carries into a level, such as weapons, armor and implants.
/// </summary>
/// <param name="Armor">The armor pickup the player wears, or 0 if the player doesn't wear any.</param>
/// <param name="Ammo">The ammunition of each weapon the player holds besides the pistols, keyed by the weapon pickup.</param>
/// <param name="Implants">The implants the player carries.</param>
/// <param name="Items">The retained utility items and their counts.</param>
public sealed record PlayerLoadout(KnownObject Armor, IReadOnlyDictionary<KnownObject, int> Ammo, PlayerImplants Implants,
    IReadOnlyDictionary<KnownObject, int> Items)
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PlayerLoadout"/> class without utility items.
    /// </summary>
    /// <param name="armor">The armor pickup the player wears, or 0 if the player doesn't wear any.</param>
    /// <param name="ammo">The ammunition of each weapon besides the pistols, keyed by the weapon pickup.</param>
    /// <param name="implants">The implants the player carries.</param>
    public PlayerLoadout(KnownObject armor, IReadOnlyDictionary<KnownObject, int> ammo, PlayerImplants implants)
        : this(armor, ammo, implants, ReadOnlyDictionary<KnownObject, int>.Empty)
    {
    }
}
