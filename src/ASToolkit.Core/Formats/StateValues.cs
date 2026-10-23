using System.Runtime.CompilerServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents one value for each animation state of an object.
/// </summary>
[InlineArray(Count)]
public struct StateValues
{
    /// <summary>
    /// Represents the number of animation states of an object.
    /// </summary>
    public const int Count = 17;

    private int _first;
}
