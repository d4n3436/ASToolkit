namespace ASToolkit.Core.Simulation;

/// <summary>
/// Represents when the level script's state changes happened in a playthrough, such as the breaker opening the doors or a generator dropping the fences.
/// </summary>
/// <param name="DoorsInitiallyLocked">Whether the doors were locked when the playthrough started.</param>
/// <param name="DoorsOpenedAt">The time the breaker opened the locked doors, or null when the level doesn't have a breaker or it wasn't reached.</param>
/// <param name="FencesDownAt">The time a generator first dropped the fences, or null when the level doesn't have a generator or no generator was destroyed.</param>
/// <param name="BlackoutAt">The time the blackout trigger turned off the lights and locked the doors again, or null when the level doesn't have a blackout trigger or it wasn't reached.</param>
public readonly record struct ScriptTimeline(bool DoorsInitiallyLocked, int? DoorsOpenedAt, int? FencesDownAt, int? BlackoutAt);
