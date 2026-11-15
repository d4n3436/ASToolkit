using System.Collections.Frozen;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal sealed class ControlSettings
{
    public const int LeftButton = 1;
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

    public static ControlSettings Default { get; } = new();

    public ControlBinding Left { get; init; } = new(LeftKey, LeftKey);

    public ControlBinding Right { get; init; } = new(RightKey, RightKey);

    public ControlBinding Up { get; init; } = new(UpKey, UpKey);

    public ControlBinding Down { get; init; } = new(DownKey, DownKey);

    public ControlBinding First { get; init; } = new(LeftButton, LeftButton);

    public ControlBinding Second { get; init; } = new(RightButton, RightButton);

    public int Prev { get; init; } = PrevKey;

    public int Next { get; init; } = NextKey;

    public bool HoldsFirst { get; init; } = true;

    public bool HoldsSecond { get; init; } = true;

    public bool Relative { get; init; }

    // Reads the first character for movement keys and a key name for the actions, falling back on the executable's default strings
    [EngineRoutine(0x00408ff0)]
    public static ControlSettings Read(string path)
    {
        var values = ReadSection(File.ReadAllLines(path, GameTextEncoding.Cyrillic));

        return new ControlSettings
        {
            Left = new ControlBinding(LeftKey, GetFirstCharacter(values, "Left", "%")),
            Right = new ControlBinding(RightKey, GetFirstCharacter(values, "Right", "'")),
            Up = new ControlBinding(UpKey, GetFirstCharacter(values, "Up", "&")),
            Down = new ControlBinding(DownKey, GetFirstCharacter(values, "Down", "\u0004" + "8")),
            Relative = GetInteger(values, "Relative", 0) != 0,
            First = new ControlBinding(ParseKey(GetString(values, "First", "LBUTTON")), LeftButton),
            Second = new ControlBinding(ParseKey(GetString(values, "Second", "RBUTTON")), RightButton),
            Prev = ParseKey(GetString(values, "Prev", "[")),
            Next = ParseKey(GetString(values, "Next", "]"))
        };
    }

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
