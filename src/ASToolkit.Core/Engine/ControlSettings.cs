using System.Collections.Frozen;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

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
    private const int PrevKey = 0x5B;
    private const int NextKey = 0x5D;
    private const string Section = "control";

    private static readonly FrozenDictionary<string, int> KeyNames = new Dictionary<string, int>
    {
        ["LBUTTON"] = LeftButton,
        ["RBUTTON"] = RightButton,
        ["["] = 0x5B,
        ["]"] = 0x5C,
        ["LEFT"] = LeftKey,
        ["RIGHT"] = RightKey,
        ["UP"] = UpKey,
        ["DOWN"] = DownKey,
        ["INSERT"] = 0x2D,
        ["DELETE"] = 0x2E,
        ["HOME"] = 0x24,
        ["END"] = 0x23,
        ["PGUP"] = 0x21,
        ["PGDN"] = 0x22,
        ["SHIFT"] = 0x10,
        ["CTRL"] = 0x11,
        ["F1"] = 0x70,
        ["F2"] = 0x71,
        ["F3"] = 0x72,
        ["F4"] = 0x73,
        ["F5"] = 0x74,
        ["F6"] = 0x75,
        ["F7"] = 0x76,
        ["F8"] = 0x77,
        ["F9"] = 0x78,
        ["F10"] = 0x79,
        ["F11"] = 0x7A,
        ["F12"] = 0x7B
    }.ToFrozenDictionary();

    /// <summary>
    /// Gets the executable's default controls.
    /// </summary>
    public static ControlSettings Default { get; } = new();

    internal ControlSettings Clone() => (ControlSettings)MemberwiseClone();

    internal ControlBinding Left { get; init; } = new(LeftKey, LeftKey);

    internal ControlBinding Right { get; init; } = new(RightKey, RightKey);

    internal ControlBinding Up { get; init; } = new(UpKey, UpKey);

    internal ControlBinding Down { get; init; } = new(DownKey, DownKey);

    internal ControlBinding First { get; set; } = new(LeftButton, LeftButton);

    internal ControlBinding Second { get; set; } = new(RightButton, RightButton);

    /// <summary>
    /// Gets the key that selects the previous weapon.
    /// </summary>
    public int Prev { get; init; } = PrevKey;

    /// <summary>
    /// Gets the key that selects the next weapon.
    /// </summary>
    public int Next { get; init; } = NextKey;

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
    public bool IsMovementRelativeToCursor { get; init; }

    /// <summary>
    /// Reads the controls from a configuration file, with the executable's defaults for missing keys.
    /// </summary>
    /// <param name="path">The path of AlienShooter.cfg.</param>
    /// <returns>The controls.</returns>
    public static ControlSettings Read(string path)
    {
        var values = ReadSection(File.ReadAllLines(path, GameTextEncoding.Cyrillic));

        return new ControlSettings
        {
            Left = new ControlBinding(LeftKey, GetFirstCharacter(values, "Left", "%")),
            Right = new ControlBinding(RightKey, GetFirstCharacter(values, "Right", "'")),
            Up = new ControlBinding(UpKey, GetFirstCharacter(values, "Up", "&")),
            Down = new ControlBinding(DownKey, GetFirstCharacter(values, "Down", "\u0004" + "8")),
            IsMovementRelativeToCursor = GetInteger(values, "Relative", 0) != 0,
            First = new ControlBinding(ParseKey(GetString(values, "First", "LBUTTON")), LeftButton),
            Second = new ControlBinding(ParseKey(GetString(values, "Second", "RBUTTON")), RightButton),
            Prev = ParseKey(GetString(values, "Prev", "[")),
            Next = ParseKey(GetString(values, "Next", "]"))
        };
    }

    /// <summary>
    /// Returns the virtual key of a key name.
    /// </summary>
    /// <param name="name">The key name.</param>
    /// <returns>The virtual key, or 0 for an unknown name.</returns>
    [EngineRoutine(0x00426370)]
    public static int ParseKey(string name)
    {
        string upper = name.ToUpperInvariant();

        if (KeyNames.TryGetValue(upper, out int code))
            return code;

        return upper.Length == 0 ? 0 : (sbyte)upper[0];
    }

    private static Dictionary<string, string> ReadSection(string[] lines)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool inSection = false;
        foreach (string line in lines)
        {
            string text = line.Trim();

            if (text.StartsWith('['))
            {
                int end = text.IndexOf(']');
                inSection = end > 0 && text[1..end].Trim().Equals(Section, StringComparison.OrdinalIgnoreCase);

                continue;
            }

            int separator = text.IndexOf('=');

            if (!inSection || separator <= 0 || text.StartsWith(';'))
                continue;

            values.TryAdd(text[..separator].Trim(), text[(separator + 1)..].Trim());
        }

        return values;
    }

    private static string GetString(Dictionary<string, string> values, string key, string fallback) =>
        values.GetValueOrDefault(key, fallback);

    private static int GetFirstCharacter(Dictionary<string, string> values, string key, string fallback)
    {
        string value = GetString(values, key, fallback);

        return value.Length == 0 ? 0 : (sbyte)GameTextEncoding.Cyrillic.GetBytes(value[..1])[0];
    }

    private static int GetInteger(Dictionary<string, string> values, string key, int fallback)
    {
        if (!values.TryGetValue(key, out string? value))
            return fallback;

        int length = 0;
        while (length < value.Length && (char.IsAsciiDigit(value[length]) || length == 0 && value[0] == '-'))
        {
            length++;
        }

        return int.TryParse(value.AsSpan(0, length), out int number) ? number : 0;
    }
}
