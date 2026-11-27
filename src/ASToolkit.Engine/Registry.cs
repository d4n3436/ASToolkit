using System.Globalization;
using System.Text;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the registry values that carry the player's state between levels.
/// </summary>
public sealed class Registry
{
    private const int MaxStackText = 256;

    private readonly Dictionary<string, string> _values;

    /// <summary>
    /// Initializes a new instance of the <see cref="Registry"/> class as an empty registry.
    /// </summary>
    /// <param name="defaultPath">The registry path named in the game's configuration.</param>
    public Registry(string defaultPath)
    {
        DefaultPath = defaultPath;
        _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Registry"/> class that reads and writes the values of another registry under a different path.
    /// </summary>
    /// <param name="path">The registry path.</param>
    /// <param name="registry">The registry whose values are shared.</param>
    public Registry(string path, Registry registry)
    {
        DefaultPath = path;
        _values = registry._values;
    }

    /// <summary>
    /// Gets the registry path named in the game's configuration.
    /// </summary>
    public string DefaultPath { get; }

    /// <summary>
    /// Gets every value of the registry, keyed by its path and name.
    /// </summary>
    public IReadOnlyDictionary<string, string> Values => _values;

    /// <summary>
    /// Reads the registry path from the game's configuration file.
    /// </summary>
    /// <param name="fileSystem">The file system that holds the configuration file.</param>
    /// <param name="programPath">The path of the game's executable.</param>
    /// <returns>The registry path, or SOFTWARE\Gromada\ and the executable's name if the file doesn't set one.</returns>
    public static string ReadDefaultPath(IGameFileSystem fileSystem, string programPath)
    {
        var programName = Map.GetProgramName(programPath);

        return ReadDefaultPath(Map.LoadConfig(fileSystem, programName), programName);
    }

    internal static string ReadDefaultPath(Profile config, ReadOnlySpan<byte> programName) =>
        config.TryGetString(Map.CommonSection, "RegPath"u8, out var path)
            ? GameTextEncoding.Cyrillic.GetString(path.Span)
            : $@"SOFTWARE\Gromada\{GameTextEncoding.Cyrillic.GetString(programName)}";

    /// <summary>
    /// Gets the value stored under a name of the path.
    /// </summary>
    /// <param name="name">The value name.</param>
    /// <param name="defaultValue">The text to return if no value is stored.</param>
    /// <returns>The stored text, or <paramref name="defaultValue"/> if no value is stored.</returns>
    [EngineRoutine(0x0042cf70)]
    public string Get(string name, string defaultValue) => _values.GetValueOrDefault(GetKey(name), defaultValue);

    /// <summary>
    /// Gets the value stored under a name of the default path, as a number.
    /// </summary>
    /// <param name="name">The value name.</param>
    /// <param name="fallback">The number when the value is missing.</param>
    /// <returns>The number.</returns>
    [EngineRoutine(0x0042d130)]
    public int GetInt(string name, int fallback)
    {
        string text = Get(name, fallback.ToString(CultureInfo.InvariantCulture));
        var bytes = text.Length <= MaxStackText ? stackalloc byte[text.Length] : new byte[text.Length];

        return CRuntime.ParseInteger(bytes[..Encoding.ASCII.GetBytes(text, bytes)]);
    }

    /// <summary>
    /// Stores a value under a name of the path.
    /// </summary>
    /// <param name="name">The value name.</param>
    /// <param name="value">The text to store.</param>
    [EngineRoutine(0x0042d1f0)]
    public void Set(string name, string value) => _values[GetKey(name)] = value;

    /// <summary>
    /// Stores a number under a name of the default path.
    /// </summary>
    /// <param name="name">The value name.</param>
    /// <param name="value">The number to store.</param>
    [EngineRoutine(0x0042d280)]
    public void SetNumber(string name, int value) => Set(name, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Removes the value stored under a name of the path.
    /// </summary>
    /// <param name="name">The value name.</param>
    [EngineRoutine(0x0042d310)]
    public void DeleteKey(string name) => _values.Remove(GetKey(name));

    private string GetKey(string name) => $"{DefaultPath}\\{name}";
}
