namespace ASToolkit.Core.Routes;

/// <summary>
/// Represents the ways the tour had to break the level's rules to reach every objective.
/// </summary>
/// <param name="UsedSecretWalls">A value indicating whether the tour went through secret walls, spawn walls or monster holes.</param>
/// <param name="ForcedGates">A value indicating whether the tour opened locked doors or fences without their key.</param>
/// <param name="Unreached">The number of objectives the tour never reached.</param>
public sealed record FallbackUsage(bool UsedSecretWalls, bool ForcedGates, int Unreached);
