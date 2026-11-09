using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Simulation;

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
    /// Represents the most implants of one kind the player can carry.
    /// </summary>
    public const int MaxCount = 3;

    private static readonly Dictionary<KnownObject, int> DamagePercents = new()
    {
        [(KnownObject)168] = 120,
        [(KnownObject)100] = 120,
        [(KnownObject)156] = 110,
        [(KnownObject)113] = 110,
        [(KnownObject)112] = 120,
        [(KnownObject)904] = 130,
        [(KnownObject)129] = 110,
    };

    internal int ScaleHealth(int health) => Repeat(health, Life, 1263, 1000);

    internal float ScaleSpeed(float speed) => Repeat((int)speed, Speed, 115, 100);

    internal int ScaleAmmo(int ammo) => Repeat(ammo, Strength, 13, 10);

    internal float ScaleAim(float aim) => Repeat((int)aim, Accuracy, 6, 10);

    internal int ScaleDamage(KnownObject payload, int damage) => DamagePercents.TryGetValue(payload, out int percent) ? Repeat(damage, Accuracy, percent, 100) : damage;

    // Apply the scripts' integer scaling once per implant
    private static int Repeat(int value, int count, int numerator, int denominator)
    {
        for (int step = 0; step < count; step++)
        {
            value = value * numerator / denominator;
        }

        return value;
    }
}
