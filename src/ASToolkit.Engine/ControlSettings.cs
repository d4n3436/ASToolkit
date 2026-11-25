using System.Text;
using ASToolkit.Engine.IO;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the [control] section of AlienShooter.cfg, with defaults for missing keys.
/// </summary>
public sealed class ControlSettings
{
    private const string ConfigFile = "AlienShooter.cfg";
    private const int MaxKeyNameLength = 16;

    private ControlSettings()
    {
    }

    /// <summary>
    /// Gets the key that selects the previous weapon.
    /// </summary>
    public VirtualKey Prev { get; private init; }

    /// <summary>
    /// Gets the key that selects the next weapon.
    /// </summary>
    public VirtualKey Next { get; private init; }

    /// <summary>
    /// Gets a value indicating whether the first action repeats while held.
    /// </summary>
    public bool IsFirstRepeatedWhileHeld { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the second action repeats while held.
    /// </summary>
    public bool IsSecondRepeatedWhileHeld { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the movement keys move relative to the cursor.
    /// </summary>
    public bool IsMovementRelativeToCursor { get; private init; }

    internal ControlBinding Left { get; private init; }

    internal ControlBinding Right { get; private init; }

    internal ControlBinding Up { get; private init; }

    internal ControlBinding Down { get; private init; }

    internal ControlBinding First { get; set; }

    internal ControlBinding Second { get; set; }

    private static ReadOnlySpan<byte> Section => "control"u8;

    /// <summary>
    /// Reads the controls from a game's AlienShooter.cfg, using defaults for missing keys.
    /// </summary>
    /// <param name="fileSystem">The file system that holds the game's files.</param>
    /// <returns>The controls.</returns>
    public static ControlSettings Read(IGameFileSystem fileSystem)
    {
        var config = new Profile(fileSystem);
        config.Load(ConfigFile);
        return new ControlSettings
        {
            Left = new ControlBinding(VirtualKey.Left, GetFirstCharacter(config, "Left"u8, "%"u8)),
            Right = new ControlBinding(VirtualKey.Right, GetFirstCharacter(config, "Right"u8, "'"u8)),
            Up = new ControlBinding(VirtualKey.Up, GetFirstCharacter(config, "Up"u8, "&"u8)),
            Down = new ControlBinding(VirtualKey.Down, GetFirstCharacter(config, "Down"u8, "\u00048"u8)),
            IsMovementRelativeToCursor = config.GetInt(Section, "Relative"u8, 0) != 0,
            First = new ControlBinding(ParseKey(GetString(config, "First"u8, "LBUTTON"u8)), VirtualKey.LeftButton),
            Second = new ControlBinding(ParseKey(GetString(config, "Second"u8, "RBUTTON"u8)), VirtualKey.RightButton),
            Prev = ParseKey(GetString(config, "Prev"u8, "["u8)),
            Next = ParseKey(GetString(config, "Next"u8, "]"u8))
        };
    }

    [EngineRoutine(0x00426370)]
    private static VirtualKey ParseKey(ReadOnlySpan<byte> name)
    {
        Span<byte> upper = name.ToArray();
        CRuntime.ToUpperInPlace(upper);

        Span<char> text = stackalloc char[MaxKeyNameLength];
        text = text[..Encoding.ASCII.GetChars(upper[..Math.Min(upper.Length, MaxKeyNameLength)], text)];

        return text switch
        {
            "LBUTTON" => VirtualKey.LeftButton,
            "RBUTTON" => VirtualKey.RightButton,
            "[" => VirtualKey.LeftWindows,
            "]" => VirtualKey.RightWindows,
            "LEFT" => VirtualKey.Left,
            "RIGHT" => VirtualKey.Right,
            "UP" => VirtualKey.Up,
            "DOWN" => VirtualKey.Down,
            "INSERT" => VirtualKey.Insert,
            "DELETE" => VirtualKey.Delete,
            "HOME" => VirtualKey.Home,
            "END" => VirtualKey.End,
            "PGUP" => VirtualKey.PageUp,
            "PGDN" => VirtualKey.PageDown,
            "SHIFT" => VirtualKey.Shift,
            "CTRL" => VirtualKey.Control,
            "F1" => VirtualKey.F1,
            "F2" => VirtualKey.F2,
            "F3" => VirtualKey.F3,
            "F4" => VirtualKey.F4,
            "F5" => VirtualKey.F5,
            "F6" => VirtualKey.F6,
            "F7" => VirtualKey.F7,
            "F8" => VirtualKey.F8,
            "F9" => VirtualKey.F9,
            "F10" => VirtualKey.F10,
            "F11" => VirtualKey.F11,
            "F12" => VirtualKey.F12,
            "" => VirtualKey.None,
            _ => (VirtualKey)(sbyte)upper[0]
        };
    }

    private static ReadOnlySpan<byte> GetString(Profile config, ReadOnlySpan<byte> key, ReadOnlySpan<byte> defaultValue) =>
        config.TryGetString(Section, key, out var value) ? value.Span : defaultValue;

    private static VirtualKey GetFirstCharacter(Profile config, ReadOnlySpan<byte> key, ReadOnlySpan<byte> defaultValue)
    {
        var value = GetString(config, key, defaultValue);

        return value.IsEmpty ? VirtualKey.None : (VirtualKey)(sbyte)value[0];
    }
}
