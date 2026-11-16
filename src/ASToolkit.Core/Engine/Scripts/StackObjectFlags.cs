namespace ASToolkit.Core.Engine.Scripts;

/// <summary>
/// Specifies the type flags of a script value.
/// </summary>
[Flags]
public enum StackObjectFlags : byte
{
    /// <summary>
    /// No flag.
    /// </summary>
    None = 0,

    /// <summary>
    /// A text value.
    /// </summary>
    Text = 1 << 0,

    /// <summary>
    /// A number value.
    /// </summary>
    Number = 1 << 1,

    /// <summary>
    /// An element of an array.
    /// </summary>
    ArrayElement = 1 << 2,

    /// <summary>
    /// A value the script gives at compile time, kept in saved script states.
    /// </summary>
    Initialized = 1 << 3,

    /// <summary>
    /// A handle of a sprite, cleared when the sprite is deleted.
    /// </summary>
    Sprite = 1 << 4,

    /// <summary>
    /// A variable declared with <c>*</c>, indexed from the slot its value names.
    /// </summary>
    Pointer = 1 << 5,

    /// <summary>
    /// A variable declared without an initial value.
    /// </summary>
    NotInitialized = 1 << 6
}
