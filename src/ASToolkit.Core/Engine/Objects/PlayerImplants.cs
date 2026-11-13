using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents the implants the player carries, up to three of each.
/// </summary>
/// <param name="Life">The number of life implants.</param>
/// <param name="Strength">The number of strength implants.</param>
/// <param name="Speed">The number of speed implants.</param>
/// <param name="Accuracy">The number of accuracy implants.</param>
public readonly record struct PlayerImplants(int Life, int Strength, int Speed, int Accuracy)
{
    /// <summary>
    /// The maximum number of implants of one kind the player can carry.
    /// </summary>
    public const int MaxCount = 3;

    private static readonly Dictionary<KnownObject, int> DamagePercents = new()
    {
        [KnownObject.PistolBullet] = 120,
        [KnownObject.ShotgunPellet] = 120,
        [KnownObject.MinigunBullet] = 110,
        [KnownObject.Rocket] = 110,
        [KnownObject.PlasmaBullet] = 120,
        [KnownObject.FlamethrowerAttack] = 130,
        [KnownObject.MagmaBullet] = 110
    };

    internal int ScaleHealth(int health) => Repeat(health, Life, 1263, 1000);

    internal float ScaleSpeed(float speed) => Repeat((int)speed, Speed, 115, 100);

    internal int ScaleAmmo(int ammo) => Repeat(ammo, Strength, 13, 10);

    internal float ScaleAim(float aim) => Repeat((int)aim, Accuracy, 6, 10);

    internal int ScaleDamage(KnownObject payload, int damage) => DamagePercents.TryGetValue(payload, out int percent) ? Repeat(damage, Accuracy, percent, 100) : damage;

    private static int Repeat(int value, int count, int numerator, int denominator)
    {
        for (int step = 0; step < count; step++)
        {
            value = value * numerator / denominator;
        }

        return value;
    }
}
