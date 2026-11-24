using System.Globalization;
using System.Text;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the registry values that carry the player's state between levels.
/// </summary>
public sealed class ScriptRegistry
{
    private const int MaxStackText = 256;
    private const string RegPathSetting = "RegPath=";

    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptRegistry"/> class as an empty registry.
    /// </summary>
    /// <param name="defaultPath">The registry path named in the game's configuration.</param>
    public ScriptRegistry(string defaultPath)
    {
        DefaultPath = defaultPath;
    }

    /// <summary>
    /// Gets the registry path named in the game's configuration.
    /// </summary>
    public string DefaultPath { get; }

    /// <summary>
    /// Reads the registry path from the game's configuration file.
    /// </summary>
    /// <param name="configPath">The path of the configuration file.</param>
    /// <returns>The registry path, or an empty string if the file doesn't name one.</returns>
    public static string ReadDefaultPath(string configPath)
    {
        foreach (string line in File.ReadLines(configPath, GameTextEncoding.Cyrillic))
        {
            var text = line.AsSpan().Trim();

            if (text.StartsWith(RegPathSetting, StringComparison.OrdinalIgnoreCase))
                return text[RegPathSetting.Length..].Trim().ToString();
        }

        return string.Empty;
    }

    /// <summary>
    /// Gets the value stored under a name of the default path, as a number.
    /// </summary>
    /// <param name="name">The value name.</param>
    /// <param name="fallback">The number when the value is missing.</param>
    /// <returns>The number.</returns>
    [EngineRoutine(0x0042d130)]
    public int GetNumber(string name, int fallback)
    {
        return ParseLong(Get(DefaultPath, name, fallback.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// Gets the value stored under a name of a path.
    /// </summary>
    /// <param name="path">The registry path.</param>
    /// <param name="name">The value name.</param>
    /// <param name="defaultValue">The text to return if no value is stored.</param>
    /// <returns>The stored text, or <paramref name="defaultValue"/> if no value is stored.</returns>
    [EngineRoutine(0x0042cf70)]
    public string Get(string path, string name, string defaultValue) => _values.GetValueOrDefault(GetKey(path, name), defaultValue);

    /// <summary>
    /// Stores a value under a name of the default path.
    /// </summary>
    /// <param name="name">The value name.</param>
    /// <param name="value">The text to store.</param>
    public void Set(string name, string value) => Set(DefaultPath, name, value);

    /// <summary>
    /// Stores a number under a name of the default path.
    /// </summary>
    /// <param name="name">The value name.</param>
    /// <param name="value">The number to store.</param>
    [EngineRoutine(0x0042d280)]
    public void SetNumber(string name, int value) => Set(name, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Stores a value under a name of a path.
    /// </summary>
    /// <param name="path">The registry path.</param>
    /// <param name="name">The value name.</param>
    /// <param name="value">The text to store.</param>
    [EngineRoutine(0x0042d1f0)]
    public void Set(string path, string name, string value) => _values[GetKey(path, name)] = value;

    /// <summary>
    /// Removes the value stored under a name of a path.
    /// </summary>
    /// <param name="path">The registry path.</param>
    /// <param name="name">The value name.</param>
    [EngineRoutine(0x0042d310)]
    public void Delete(string path, string name) => _values.Remove(GetKey(path, name));

    private static string GetKey(string path, string name) => $"{path}\\{name}";

    private static int ParseLong(ReadOnlySpan<char> text)
    {
        var bytes = text.Length <= MaxStackText ? stackalloc byte[text.Length] : new byte[text.Length];
        return CRuntime.ParseInteger(bytes[..Encoding.ASCII.GetBytes(text, bytes)]);
    }
}
