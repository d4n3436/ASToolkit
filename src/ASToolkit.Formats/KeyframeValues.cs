using System.Runtime.CompilerServices;

namespace ASToolkit.Formats;

/// <summary>
/// Represents one value for each keyframe of a weapon animation.
/// </summary>
/// <typeparam name="T">The type of the values.</typeparam>
[InlineArray(WeaponRecord.KeyframeCount)]
public struct KeyframeValues<T>
    where T : unmanaged
{
    private T _first;
}
