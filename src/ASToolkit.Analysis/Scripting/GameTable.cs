using System.Diagnostics.CodeAnalysis;
using ASToolkit.Engine.Scripts;
using ASToolkit.Formats;

namespace ASToolkit.Analysis.Scripting;

/// <summary>
/// Provides the number arrays a game script declares, such as the prices and level money of common_table.lgc.
/// </summary>
public sealed class GameTable
{
    /// <summary>
    /// Represents the file name of the script in the maps folder that declares the prices and the level money.
    /// </summary>
    public const string CommonFileName = "common_table.lgc";
    private const string TableName = "table.lgc";

    private readonly Dictionary<string, int[]> _arrays;

    private GameTable(Dictionary<string, int[]> arrays) => _arrays = arrays;

    /// <summary>
    /// Gets the path of the common table script, relative to the game folder.
    /// </summary>
    public static string CommonPath { get; } = Path.Combine("Maps", CommonFileName);

    /// <summary>
    /// Gets the names of the arrays the script declares.
    /// </summary>
    public IReadOnlyCollection<string> Names => _arrays.Keys;

    /// <summary>
    /// Reads the arrays of the game script at a path.
    /// </summary>
    /// <param name="path">The path of the game script.</param>
    /// <returns>The table of the arrays the script declares.</returns>
    public static GameTable Load(string path) => Read(File.ReadAllBytes(path));

    /// <summary>
    /// Reads the arrays of the common table script of a game.
    /// </summary>
    /// <param name="gameFolder">The game folder.</param>
    /// <returns>The table of the arrays the common table script declares.</returns>
    public static GameTable LoadCommon(string gameFolder) => Load(Path.Combine(gameFolder, CommonPath));

    /// <summary>
    /// Returns the name of the array that holds the money paid at the end of each level of a campaign.
    /// </summary>
    /// <param name="campaignFolder">The folder of the campaign's maps inside the maps folder, empty for the original campaign.</param>
    /// <returns>The name of the level money array.</returns>
    public static string GetLevelMoneyName(string campaignFolder) => campaignFolder.Replace('\\', '/').Trim('/').ToUpperInvariant() switch
    {
        "ADDON" => "LevelMoneyAddon",
        "ADDON2" => "LevelMoneyAddon2",
        "RANDOM" => "LevelMoneyRandom",
        _ => "LevelMoney"
    };

    /// <summary>
    /// Reads the arrays of a game script from its text.
    /// </summary>
    /// <param name="text">The text of the game script.</param>
    /// <returns>The table of the arrays the script declares.</returns>
    public static GameTable Parse(string text) => Read(GameTextEncoding.Cyrillic.GetBytes(text));

    /// <summary>
    /// Reads the arrays of a game script from its bytes.
    /// </summary>
    /// <param name="text">The bytes of the game script.</param>
    /// <returns>The table of the arrays the script declares.</returns>
    public static GameTable Parse(ReadOnlyMemory<byte> text) => Read(text);

    /// <summary>
    /// Gets an array with the given name.
    /// </summary>
    /// <param name="name">The name of the array.</param>
    /// <returns>The array.</returns>
    /// <exception cref="KeyNotFoundException">The script doesn't declare an array with the given name.</exception>
    public IReadOnlyList<int> GetArray(string name) =>
        _arrays.TryGetValue(name, out int[]? values) ? values : throw new KeyNotFoundException($"Could not find array '{name}'.");

    /// <summary>
    /// Attempts to find an array with the given name.
    /// </summary>
    /// <param name="name">The name of the array.</param>
    /// <param name="values">The array when it is found; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the script declares an array with the given name; otherwise, <see langword="false"/>.</returns>
    public bool TryGetArray(string name, [MaybeNullWhen(false)] out IReadOnlyList<int> values)
    {
        bool found = _arrays.TryGetValue(name, out int[]? array);
        values = array;
        return found;
    }

    private static GameTable Read(ReadOnlyMemory<byte> text)
    {
        var script = new Script(new MarkedScriptSource(null, text));
        script.Load(TableName);
        var arrays = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var (entryName, entry) in script.Objects.Items)
        {
            if (entry.Kind != ScriptObjectKind.Variable || (script.GetSlot(entry.Address).Flags & (ScriptStackFlags.ArrayElement | ScriptStackFlags.Text)) !=
                ScriptStackFlags.ArrayElement)
                continue;

            int[] values = new int[entry.Count];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = script.GetSlot(entry.Address + i).Value;
            }

            arrays[GameTextEncoding.Cyrillic.GetString(entryName.Span)] = values;
        }

        return new GameTable(arrays);
    }
}
