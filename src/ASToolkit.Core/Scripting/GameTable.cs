using System.Diagnostics.CodeAnalysis;
using ASToolkit.Core.Engine.Scripts;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Scripting;

/// <summary>
/// Provides the number arrays a game script declares, such as the prices and level money of common_table.lgc.
/// </summary>
public sealed class GameTable
{
    private const string TableName = "table.lgc";
    private readonly Dictionary<string, int[]> _arrays;

    private GameTable(Dictionary<string, int[]> arrays) => _arrays = arrays;

    /// <summary>
    /// Gets the names of the arrays the script declares.
    /// </summary>
    public IReadOnlyCollection<string> Names => _arrays.Keys;

    /// <summary>
    /// Reads the arrays of a game script.
    /// </summary>
    public static GameTable Load(string path) => Read(File.ReadAllBytes(path));

    /// <summary>
    /// Reads the arrays of a game script's text.
    /// </summary>
    public static GameTable Parse(string text) => Read(GameTextEncoding.Cyrillic.GetBytes(text));

    /// <summary>
    /// Returns the values of an array.
    /// </summary>
    public IReadOnlyList<int> GetArray(string name) =>
        _arrays.TryGetValue(name, out int[]? values) ? values : throw new KeyNotFoundException($"Array {name} not found.");

    /// <summary>
    /// Finds the values of an array, returning false when the script declares none by that name.
    /// </summary>
    public bool TryGetArray(string name, [MaybeNullWhen(false)] out IReadOnlyList<int> values)
    {
        bool found = _arrays.TryGetValue(name, out int[]? array);
        values = array;
        return found;
    }

    private static GameTable Read(ReadOnlyMemory<byte> text)
    {
        var script = new Script();
        script.Load(new MarkedScriptSource(null, text), TableName);
        var arrays = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var entry in script.Objects)
        {
            if (entry.Kind != ScriptObjectKind.Variable || (script.GetSlot(entry.Address).Flags & (StackObjectFlags.ArrayElement | StackObjectFlags.Text)) !=
                StackObjectFlags.ArrayElement)
                continue;

            int[] values = new int[entry.Count];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = script.GetSlot(entry.Address + index).Value;
            }

            arrays[GameTextEncoding.Cyrillic.GetString(entry.Name.Span)] = values;
        }

        return new GameTable(arrays);
    }
}
