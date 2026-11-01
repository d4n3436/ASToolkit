namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents a value fitted to the original levels together with the number of samples it came from and whether too few samples made it fall
/// back to a simpler form.
/// </summary>
/// <typeparam name="T">The type of the fitted value.</typeparam>
/// <param name="Value">The fitted value.</param>
/// <param name="Samples">The number of samples the value was fitted to.</param>
/// <param name="IsBackedOff">Whether the value takes a simpler form because the samples were too few for the full one.</param>
public readonly record struct FittedValue<T>(T Value, int Samples, bool IsBackedOff);
