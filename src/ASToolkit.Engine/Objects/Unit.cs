using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a unit sprite.
/// </summary>
public class Unit : Terrain
{
    private const int AmmoScale = 64;

    /// <summary>
    /// Initializes a new instance of the <see cref="Unit"/> class.
    /// </summary>
    /// <param name="number">The number of the sprite.</param>
    /// <param name="definition">The definition of the sprite.</param>
    /// <param name="position">The position of the sprite.</param>
    /// <param name="army">The army of the sprite.</param>
    /// <param name="hitPoints">The hit points of the sprite.</param>
    /// <param name="direction">The direction of the sprite.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    public Unit(int number, SpriteDefinition definition, Vector3 position, Army army, int hitPoints, Angle direction = default, Map? map = null)
        : base(number, definition, position, army, hitPoints, direction, map)
    {
    }

    /// <summary>
    /// Gets or sets the frames the unit has spent turning while stuck.
    /// </summary>
    public int TurnTicks { get; set; }

    /// <summary>
    /// Gets the ammunition the unit holds, in 1/64 rounds.
    /// </summary>
    public int Ammo { get; private set; }

    /// <summary>
    /// Gets the whole rounds the unit holds.
    /// </summary>
    public int Rounds => Ammo / AmmoScale;

    /// <summary>
    /// Gets or sets the behavior bits of the unit.
    /// </summary>
    public WeaponBehavior Behavior { get; set; }

    /// <summary>
    /// Gets a value indicating whether the unit attacks enemies in weapon range.
    /// </summary>
    public bool IsAggressive => Behavior.HasFlag(WeaponBehavior.Aggressive);

    /// <summary>
    /// Gets a value indicating whether the unit moves toward enemies it sees.
    /// </summary>
    public bool IsActive => Behavior.HasFlag(WeaponBehavior.Active);

    /// <summary>
    /// Returns the ammunition the unit holds when full.
    /// </summary>
    /// <returns>The capacity in rounds.</returns>
    [EngineRoutine(0x0043a200)]
    public int GetAmmoCapacity()
    {
        var link = Definition.LinkDefinition;

        return link is not null && link.HasFightWeapon() ? link.AmmoCapacity : Definition.AmmoCapacity;
    }

    /// <summary>
    /// Fills the unit's ammunition.
    /// </summary>
    public void FillAmmo() => Ammo = GetAmmoCapacity() * AmmoScale;

    /// <summary>
    /// Returns the rounds the unit holds for its held weapon.
    /// </summary>
    /// <returns>The rounds.</returns>
    public int GetAmmo() => Rounds;

    /// <summary>
    /// Adds rounds to the unit's held weapon.
    /// </summary>
    /// <param name="amount">The rounds to add, negative to spend.</param>
    /// <returns>The rounds held after the addition.</returns>
    public int AddAmmo(int amount)
    {
        if (GetAmmoCapacity() == SpriteDefinition.Unlimited)
        {
            Ammo = SpriteDefinition.Unlimited * AmmoScale;

            return SpriteDefinition.Unlimited;
        }

        int ammo = Ammo + amount * AmmoScale;
        Ammo = Math.Max(0, ammo);

        if (ShouldRefillAmmo)
        {
            Ammo = GetAmmoCapacity() * AmmoScale;
        }

        return ammo;
    }

    internal override void Destruct(Map map) => map.DestructUnit(this);

    internal override int Action(Map map, int act, int var1, int var2, int var3, Sprite? target) => map.UnitAction(this, act, var1, var2, var3, target);

    internal override void MoveTact(Map map) => map.Brain.Move(this, map.FrameTime, map.FramesPerSecond);
}
