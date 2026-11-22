namespace ASToolkit.Core.Formats;

/// <summary>
/// Specifies the behavior word of a text sprite, its alignment bits and the source of its text under <see cref="LoadFrom"/>.
/// </summary>
[Flags]
public enum TextBehavior
{
    /// <summary>
    /// Text aligned to the left.
    /// </summary>
    Left = 0,

    /// <summary>
    /// Text centered across.
    /// </summary>
    CenterX = 1 << 0,

    /// <summary>
    /// Text aligned to the right.
    /// </summary>
    Right = 1 << 1,

    /// <summary>
    /// Text aligned to the top.
    /// </summary>
    Top = Left,

    /// <summary>
    /// Text aligned to the bottom.
    /// </summary>
    Bottom = 1 << 2,

    /// <summary>
    /// Text centered down.
    /// </summary>
    CenterY = 1 << 3,

    /// <summary>
    /// Text centered both ways.
    /// </summary>
    Center = CenterX | CenterY,

    /// <summary>
    /// The mask of the text's source.
    /// </summary>
    LoadFrom = 7 << 4,

    /// <summary>
    /// Text shown as written.
    /// </summary>
    NotLoad = Left,

    /// <summary>
    /// Text taken from strings.ini under the written keyword.
    /// </summary>
    StringsIni = 1 << 4,

    /// <summary>
    /// Text loaded from the written file.
    /// </summary>
    File = 2 << 4,

    /// <summary>
    /// Text taken from the script variable of the written name.
    /// </summary>
    ScriptVariable = 3 << 4,

    /// <summary>
    /// Text loaded from the file a script variable of the written name holds.
    /// </summary>
    ScriptFile = 4 << 4,

    /// <summary>
    /// Text taken from strings.ini under the keyword a script variable of the written name holds.
    /// </summary>
    ScriptStrings = 5 << 4,

    /// <summary>
    /// Text taken from the script variable of the written name on every draw.
    /// </summary>
    ScriptUpdated = 6 << 4,

    /// <summary>
    /// A blinking cursor follows the text.
    /// </summary>
    Cursor = 1 << 8
}
