using System.Globalization;
using System.Text.RegularExpressions;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Provides the integer arrays a game script declares, such as the prices and level money of common_table.lgc.
/// </summary>
public sealed partial class GameTable
{
    private readonly Dictionary<string, int[]> _arrays;

    private GameTable(Dictionary<string, int[]> arrays) => _arrays = arrays;

    /// <summary>
    /// Gets the names of the arrays the script declares.
    /// </summary>
    public IReadOnlyCollection<string> Names => _arrays.Keys;

    /// <summary>
    /// Reads the arrays of a game script.
    /// </summary>
    public static GameTable Load(string path) => Parse(GameTextEncoding.Cyrillic.GetString(File.ReadAllBytes(path)));

    /// <summary>
    /// Reads the arrays of a game script's text.
    /// </summary>
    public static GameTable Parse(string text)
    {
        string code = CommentPattern().Replace(text, "");
        var arrays = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (Match match in ArrayPattern().Matches(code))
        {
            arrays[match.Groups["name"].Value] = ParseValues(match.Groups["values"].ValueSpan);
        }

        return new GameTable(arrays);
    }

    /// <summary>
    /// Returns the values of an array.
    /// </summary>
    public IReadOnlyList<int> GetArray(string name) =>
        _arrays.TryGetValue(name, out int[]? values) ? values : throw new KeyNotFoundException($"Array {name} not found.");

    /// <summary>
    /// Finds the values of an array, returning false when the script declares none by that name.
    /// </summary>
    public bool TryGetArray(string name, out IReadOnlyList<int> values)
    {
        bool found = _arrays.TryGetValue(name, out int[]? array);
        values = array ?? [];
        return found;
    }

    private static int[] ParseValues(ReadOnlySpan<char> list)
    {
        var values = new List<int>();
        foreach (var range in list.Split(','))
        {
            var value = list[range].Trim();
            if (!value.IsEmpty)
            {
                values.Add(int.Parse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture));
            }
        }

        return values.ToArray();
    }

    [GeneratedRegex(@"//[^\n]*|/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CommentPattern();

    [GeneratedRegex(@"\bstatic\s+int\s+(?<name>\w+)\s*\[\s*\d*\s*\]\s*=\s*\{(?<values>[^}]*)\}")]
    private static partial Regex ArrayPattern();
}
