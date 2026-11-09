using System.Runtime.CompilerServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents one floating-point value for each animation state of an object.
/// </summary>
[InlineArray(StateValues.Count)]
public struct StateFloats
{
    private float _first;
}
