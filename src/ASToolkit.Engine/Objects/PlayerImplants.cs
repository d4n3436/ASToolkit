namespace ASToolkit.Engine;

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
    /// Represents the maximum number of implants of one kind the player can carry.
    /// </summary>
    public const int MaxCount = 3;

    /// <summary>
    /// Scales the rounds of an ammunition pickup by the strength implants, 13/10 for each one.
    /// </summary>
    /// <param name="ammo">The rounds of the pickup.</param>
    /// <returns>The rounds the player gets.</returns>
    public int ScaleAmmo(int ammo) => Repeat(ammo, Strength, 13, 10);

    private static int Repeat(int value, int count, int numerator, int denominator)
    {
        for (int step = 0; step < count; step++)
        {
            value = value * numerator / denominator;
        }

        return value;
    }
}
