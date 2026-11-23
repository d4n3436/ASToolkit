using System.Buffers;
using System.Text;
using ASToolkit.Engine.IO;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the [control] section of AlienShooter.cfg, with the executable's defaults for missing keys.
/// </summary>
public sealed class ControlSettings
{
    /// <summary>
    /// Represents the binding of the left mouse button.
    /// </summary>
    public const int LeftButton = 1;
    
    /// <summary>
    /// Represents the binding of the right mouse button.
    /// </summary>
    public const int RightButton = 2;
    
    private const int LeftKey = 0x25;
    private const int UpKey = 0x26;
    private const int RightKey = 0x27;
    private const int DownKey = 0x28;
    private const string ConfigFile = "AlienShooter.cfg";

    private static ReadOnlySpan<byte> Section => "control"u8;

    private ControlSettings()
    {
    }

    internal ControlBinding Left { get; private init; }

    internal ControlBinding Right { get; private init; }

    internal ControlBinding Up { get; private init; }

    internal ControlBinding Down { get; private init; }

    internal ControlBinding First { get; set; }

    internal ControlBinding Second { get; set; }

    /// <summary>
    /// Gets the key that selects the previous weapon.
    /// </summary>
    public int Prev { get; private init; }

    /// <summary>
    /// Gets the key that selects the next weapon.
    /// </summary>
    public int Next { get; private init; }

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

    /// <summary>
    /// Reads the controls from a game's AlienShooter.cfg, with the executable's defaults for missing keys.
    /// </summary>
    /// <param name="fileSystem">The file system the game's files are read from.</param>
    /// <returns>The controls.</returns>
    public static ControlSettings Read(IGameFileSystem fileSystem)
    {
        return new ControlSettings
        {
            Left = new ControlBinding(LeftKey, GetFirstCharacter(fileSystem, "Left"u8, "%"u8)),
            Right = new ControlBinding(RightKey, GetFirstCharacter(fileSystem, "Right"u8, "'"u8)),
            Up = new ControlBinding(UpKey, GetFirstCharacter(fileSystem, "Up"u8, "&"u8)),
            Down = new ControlBinding(DownKey, GetFirstCharacter(fileSystem, "Down"u8, "\u00048"u8)),
            IsMovementRelativeToCursor = ProfileFile.GetInteger(fileSystem, ConfigFile, Section, "Relative"u8, 0) != 0,
            First = new ControlBinding(ParseKey(GetString(fileSystem, "First"u8, "LBUTTON"u8)), LeftButton),
            Second = new ControlBinding(ParseKey(GetString(fileSystem, "Second"u8, "RBUTTON"u8)), RightButton),
            Prev = ParseKey(GetString(fileSystem, "Prev"u8, "["u8)),
            Next = ParseKey(GetString(fileSystem, "Next"u8, "]"u8))
        };
    }

    [EngineRoutine(0x00426370)]
    private static int ParseKey(ReadOnlySpan<byte> name)
    {
        Span<byte> upper = name.ToArray();
        CRuntime.ToUpperInPlace(upper);

        Span<char> text = new char[upper.Length];
        if (Ascii.ToUtf16(upper, text, out _) != OperationStatus.Done)
            return (sbyte)upper[0];

        return text switch
        {
            "LBUTTON" => LeftButton,
            "RBUTTON" => RightButton,
            "[" => 0x5B,
            "]" => 0x5C,
            "LEFT" => LeftKey,
            "RIGHT" => RightKey,
            "UP" => UpKey,
            "DOWN" => DownKey,
            "INSERT" => 0x2D,
            "DELETE" => 0x2E,
            "HOME" => 0x24,
            "END" => 0x23,
            "PGUP" => 0x21,
            "PGDN" => 0x22,
            "SHIFT" => 0x10,
            "CTRL" => 0x11,
            "F1" => 0x70,
            "F2" => 0x71,
            "F3" => 0x72,
            "F4" => 0x73,
            "F5" => 0x74,
            "F6" => 0x75,
            "F7" => 0x76,
            "F8" => 0x77,
            "F9" => 0x78,
            "F10" => 0x79,
            "F11" => 0x7A,
            "F12" => 0x7B,
            "" => 0,
            _ => (sbyte)upper[0]
        };
    }

    private static ReadOnlySpan<byte> GetString(IGameFileSystem fileSystem, ReadOnlySpan<byte> key, ReadOnlySpan<byte> defaultValue) =>
        ProfileFile.TryGetString(fileSystem, ConfigFile, Section, key, out var value) ? value.Span : defaultValue;

    private static int GetFirstCharacter(IGameFileSystem fileSystem, ReadOnlySpan<byte> key, ReadOnlySpan<byte> defaultValue)
    {
        var value = GetString(fileSystem, key, defaultValue);

        return value.IsEmpty ? 0 : (sbyte)value[0];
    }
}
