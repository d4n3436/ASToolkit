using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents the registry values the game's scripts read and write to carry the player between levels.
/// </summary>
public sealed class ScriptRegistry
{
    private const string RegPathSetting = "RegPath=";
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates an empty registry.
    /// </summary>
    /// <param name="defaultPath">The registry path the game's configuration names.</param>
    public ScriptRegistry(string defaultPath)
    {
        DefaultPath = defaultPath;
    }

    /// <summary>
    /// Gets the registry path the game's configuration names.
    /// </summary>
    public string DefaultPath { get; }

    /// <summary>
    /// Reads the registry path from the game's configuration file.
    /// </summary>
    /// <param name="configPath">The path of the configuration file.</param>
    /// <returns>The registry path, or an empty string when the file names none.</returns>
    public static string ReadDefaultPath(string configPath)
    {
        foreach (string line in File.ReadLines(configPath, GameTextEncoding.Cyrillic))
        {
            var text = line.AsSpan().Trim();

            if (text.StartsWith(RegPathSetting, StringComparison.OrdinalIgnoreCase))
                return text[RegPathSetting.Length..].Trim().ToString();
        }

        return "";
    }

    /// <summary>
    /// Gets the value stored under a name of the default path.
    /// </summary>
    /// <param name="name">The value name.</param>
    /// <returns>The stored text, or <see langword="null"/> when none is stored.</returns>
    public string? Get(string name) => Get(DefaultPath, name);

    /// <summary>
    /// Gets the value stored under a name of the default path, as a number.
    /// </summary>
    /// <param name="name">The value name.</param>
    /// <param name="fallback">The number when the value is missing.</param>
    /// <returns>The number.</returns>
    [EngineRoutine(0x0042d130)]
    public int GetNumber(string name, int fallback)
    {
        string? value = Get(name);

        return value is null ? fallback : ParseLong(value);
    }

    /// <summary>
    /// Gets the value stored under a name of a path.
    /// </summary>
    /// <param name="path">The registry path.</param>
    /// <param name="name">The value name.</param>
    /// <returns>The stored text, or <see langword="null"/> when none is stored.</returns>
    public string? Get(string path, string name) => _values.GetValueOrDefault(GetKey(path, name));

    /// <summary>
    /// Stores a value under a name of the default path.
    /// </summary>
    /// <param name="name">The value name.</param>
    /// <param name="value">The text to store.</param>
    public void Set(string name, string value) => Set(DefaultPath, name, value);

    /// <summary>
    /// Stores a value under a name of a path.
    /// </summary>
    /// <param name="path">The registry path.</param>
    /// <param name="name">The value name.</param>
    /// <param name="value">The text to store.</param>
    public void Set(string path, string name, string value) => _values[GetKey(path, name)] = value;

    /// <summary>
    /// Removes the value stored under a name of a path.
    /// </summary>
    /// <param name="path">The registry path.</param>
    /// <param name="name">The value name.</param>
    public void Delete(string path, string name) => _values.Remove(GetKey(path, name));

    private static string GetKey(string path, string name) => $"{path}\\{name}";

    private static int ParseLong(ReadOnlySpan<char> text)
    {
        text = text.TrimStart();
        bool isNegative = !text.IsEmpty && text[0] == '-';

        if (!text.IsEmpty && text[0] is '-' or '+')
        {
            text = text[1..];
        }

        int total = 0;
        for (int i = 0; i < text.Length && char.IsAsciiDigit(text[i]); i++)
        {
            total = unchecked(total * 10 + (text[i] - '0'));
        }

        return isNegative ? unchecked(-total) : total;
    }
}
