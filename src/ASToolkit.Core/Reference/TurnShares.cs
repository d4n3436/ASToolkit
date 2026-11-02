namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents the shares of steps along a critical path that go straight on, turn aside or double back.
/// </summary>
/// <param name="Straight">The share of steps that go straight on.</param>
/// <param name="Turn">The share of steps that turn aside.</param>
/// <param name="Back">The share of steps that double back.</param>
public readonly record struct TurnShares(float Straight, float Turn, float Back);
