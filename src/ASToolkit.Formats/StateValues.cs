using System.Runtime.CompilerServices;

namespace ASToolkit.Formats;

/// <summary>
/// Represents one value for each animation state of an object.
/// </summary>
/// <typeparam name="T">The type of the values.</typeparam>
[InlineArray(GameObject.StateCount)]
public struct StateValues<T>
    where T : unmanaged
{
    private T _first;
}
