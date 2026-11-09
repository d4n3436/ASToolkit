using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Simulation;

/// <summary>
/// Represents what the player carries into a level.
/// </summary>
/// <param name="Armor">The armor pickup the player wears, or 0 for none.</param>
/// <param name="Ammo">The ammunition of each weapon the player holds besides the pistols, keyed by the weapon pickup.</param>
/// <param name="Implants">The implants the player carries.</param>
public sealed record PlayerLoadout(KnownObject Armor, IReadOnlyDictionary<KnownObject, int> Ammo, PlayerImplants Implants);
