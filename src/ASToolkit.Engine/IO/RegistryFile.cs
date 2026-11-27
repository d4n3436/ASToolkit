using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ASToolkit.Engine.IO;

/// <summary>
/// Represents the registry of the game, stored in the Registry.json file of the game folder.
/// </summary>
public sealed class RegistryFile
{
    /// <summary>
    /// Represents the name of the file that holds the values.
    /// </summary>
    public const string FileName = "Registry.json";

    private static readonly JsonTypeInfo SaveTypeInfo = new JsonSerializerOptions(RegistryJsonContext.Default.Options) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }
        .GetTypeInfo<Dictionary<string, Dictionary<string, string>>>();

    private readonly IGameFileSystem? _fileSystem;
    private readonly Dictionary<string, Dictionary<string, string>> _keys = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="RegistryFile"/> class with no values, kept in memory only.
    /// </summary>
    public RegistryFile()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RegistryFile"/> class with the values stored in a file system.
    /// </summary>
    /// <param name="fileSystem">The file system that holds the file.</param>
    /// <exception cref="JsonException">The file is not a JSON object of keys that each map value names to text.</exception>
    public RegistryFile(IGameFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
        if (!fileSystem.TryRead(FileName, out var data))
            return;

        _keys = JsonSerializer.Deserialize(data.Span, RegistryJsonContext.Default.DictionaryStringDictionaryStringString)!;
    }

    /// <summary>
    /// Attempts to get a value of a key.
    /// </summary>
    /// <param name="key">The path of the key.</param>
    /// <param name="name">The name of the value.</param>
    /// <param name="value">When this method returns, the value if it is stored; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the value is stored; otherwise, <see langword="false"/>.</returns>
    public bool TryGetValue(string key, string name, [MaybeNullWhen(false)] out string value)
    {
        if (_keys.TryGetValue(key, out var values))
            return values.TryGetValue(name, out value);

        value = null;

        return false;
    }

    /// <summary>
    /// Stores a value of a key and writes the file, if there is one.
    /// </summary>
    /// <param name="key">The path of the key.</param>
    /// <param name="name">The name of the value.</param>
    /// <param name="value">The value.</param>
    public void SetValue(string key, string name, string value)
    {
        ref var values = ref CollectionsMarshal.GetValueRefOrAddDefault(_keys, key, out _);
        values ??= [];
        values[name] = value;
        Save();
    }

    /// <summary>
    /// Removes a value of a key and writes the file, if there is one.
    /// </summary>
    /// <param name="key">The path of the key.</param>
    /// <param name="name">The name of the value.</param>
    public void DeleteValue(string key, string name)
    {
        if (!_keys.TryGetValue(key, out var values) || !values.Remove(name))
            return;

        Save();
    }

    private void Save()
    {
        using var stream = _fileSystem?.OpenWrite(FileName, false);
        if (stream is null)
            return;

        JsonSerializer.Serialize(stream, _keys, SaveTypeInfo);
    }
}
