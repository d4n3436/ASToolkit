using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the player's body sprite.
/// </summary>
public sealed class Man : Unit
{
    private const int MaxWeaponSlot = 9;

    /// <summary>
    /// Initializes a new instance of the <see cref="Man"/> class.
    /// </summary>
    /// <param name="number">The number of the sprite.</param>
    /// <param name="definition">The definition of the sprite.</param>
    /// <param name="position">The position of the sprite.</param>
    /// <param name="army">The army of the sprite.</param>
    /// <param name="hitPoints">The hit points of the sprite.</param>
    /// <param name="direction">The direction of the sprite.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    public Man(int number, SpriteDefinition definition, Vector3 position, Army army, int hitPoints, Angle direction = default, Map? map = null)
        : base(number, definition, position, army, hitPoints, direction, map)
    {
    }

    /// <summary>
    /// Gets the ammunition the player's body keeps for the slots it does not hold.
    /// </summary>
    public int[] SlotAmmo => field ??= new int[MaxWeaponSlot + 1];

    internal override void Destruct(Map map) => map.DestructMan(this);

    internal override int Action(Map map, int act, int var1, int var2, int var3, Sprite? target) => map.ManAction(this, act, var1, var2, var3, target);

    internal override void MoveTact(Map map) => map.PlayerMovement.MoveTact(this, map.FrameTime);
}
