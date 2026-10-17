using System.Text.RegularExpressions;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Routes;

/// <summary>
/// Provides the reading of level scripts.
/// </summary>
public static partial class LevelScriptReader
{
    /// <summary>
    /// Reads the facts of a level script, or returns empty facts when the file does not exist.
    /// </summary>
    public static LevelScriptSummary Read(string path) => File.Exists(path) ? Parse(File.ReadAllBytes(path)) : new LevelScriptSummary();

    /// <summary>
    /// Reads the facts of a level script from its bytes.
    /// </summary>
    public static LevelScriptSummary Parse(ReadOnlySpan<byte> data)
    {
        string text = GameTextEncoding.Cyrillic.GetString(data);
        text = BlockComment.Replace(text, "");
        text = LineComment.Replace(text, "");
        var start = StartBlock.Match(text);
        TriggerArea? blackout = null;
        TriggerArea? exit = null;
        var checkpoints = new List<TriggerArea>();
        foreach (Match match in DistanceBlock.Matches(text))
        {
            var area = new TriggerArea(int.Parse(match.Groups[1].ValueSpan), int.Parse(match.Groups[2].ValueSpan), int.Parse(match.Groups[3].ValueSpan));
            string body = match.Groups[4].Value;
            if (body.Contains("LightOff", StringComparison.Ordinal))
            {
                blackout = area;
            }

            if (body.Contains("StartPlayerX", StringComparison.Ordinal))
            {
                checkpoints.Add(area);
            }

            if (body.Contains("Win()", StringComparison.Ordinal))
            {
                exit = area;
            }
        }

        return new LevelScriptSummary
        {
            DoorsLocked = start.Success && DoorsClosed.IsMatch(start.Groups[1].ValueSpan),
            Blackout = blackout,
            Checkpoints = checkpoints,
            DynamiteWin = text.Contains($"GetVidData({(int)KnownObject.DynamiteSite}", StringComparison.Ordinal),
            Exit = exit
        };
    }

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockComment { get; }

    [GeneratedRegex(@"//[^\n]*")]
    private static partial Regex LineComment { get; }

    [GeneratedRegex(@"iff\(1\)\s*(\{[^}]*\}|[^;]*;)")]
    private static partial Regex StartBlock { get; }

    [GeneratedRegex(@"DoorsIsOpen\s*=\s*0")]
    private static partial Regex DoorsClosed { get; }

    [GeneratedRegex(@"iff\(\s*SizeTo\(Flagman\(0\),(-?\d+),(-?\d+)\)\s*<\s*(\d+)\s*\)\s*\{(.*?)\n  \}", RegexOptions.Singleline)]
    private static partial Regex DistanceBlock { get; }
}
