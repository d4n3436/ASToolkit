namespace ASToolkit.Engine.Scripts;

/// <summary>
/// Specifies the kinds of global names in a compiled script.
/// </summary>
public enum ScriptObjectKind : byte
{
    /// <summary>
    /// No object.
    /// </summary>
    None = 0,

    /// <summary>
    /// A variable or array.
    /// </summary>
    Variable = 1,

    /// <summary>
    /// A built-in function of the engine.
    /// </summary>
    Extern = 2,

    /// <summary>
    /// A function of the script.
    /// </summary>
    Function = 3,

    /// <summary>
    /// A text constant.
    /// </summary>
    Text = 4,

    /// <summary>
    /// A number constant.
    /// </summary>
    Number = 5,

    /// <summary>
    /// A name that compiles to nothing.
    /// </summary>
    Empty = 6,

    /// <summary>
    /// A label.
    /// </summary>
    Label = 7,

    /// <summary>
    /// A label a <c>goto</c> uses before its definition.
    /// </summary>
    PendingLabel = 8
}
