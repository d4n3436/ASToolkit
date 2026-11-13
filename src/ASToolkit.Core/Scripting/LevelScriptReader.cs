using System.Text.RegularExpressions;
using System.Drawing;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Scripting;

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
        uint? blackoutTint = null;
        TriggerArea? exit = null;
        var checkpoints = new List<TriggerArea>();
        foreach (Match match in DistanceBlock.Matches(text))
        {
            var area = new TriggerArea(new Point(int.Parse(match.Groups[1].ValueSpan), int.Parse(match.Groups[2].ValueSpan)), int.Parse(match.Groups[3].ValueSpan));
            int opening = match.Index + match.Length - 1;
            int closing = FindBlockEnd(text, opening);
            if (closing < 0)
                continue;
            string body = text[(opening + 1)..closing];
            var lightOff = LightOffCall.Match(body);
            if (lightOff.Success)
            {
                blackout = area;
                blackoutTint = Convert.ToUInt32(lightOff.Groups[1].Value, lightOff.Groups[1].Value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10);
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
            ClearsMonsters = text.Contains("CompaignGameTact()", StringComparison.Ordinal),
            DoorsLocked = start.Success && DoorsClosed.IsMatch(start.Groups[1].ValueSpan),
            Blackout = blackout,
            BlackoutTint = blackoutTint,
            Checkpoints = checkpoints,
            DynamiteWin = text.Contains($"GetVidData({(int)KnownObject.DynamiteSite}", StringComparison.Ordinal),
            Exit = exit
        };
    }

    private static int FindBlockEnd(string text, int opening)
    {
        int depth = 0;
        bool quoted = false;
        for (int index = opening; index < text.Length; index++)
        {
            char value = text[index];
            if (quoted && value == '\\')
            {
                index++;
                continue;
            }
            if (value == '"')
            {
                quoted = !quoted;
            }
            if (quoted)
                continue;
            if (value == '{')
            {
                depth++;
            }
            else if (value == '}' && --depth == 0)
                return index;
        }
        return -1;
    }

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockComment { get; }

    [GeneratedRegex(@"//[^\n]*")]
    private static partial Regex LineComment { get; }

    [GeneratedRegex(@"iff\(1\)\s*(\{[^}]*\}|[^;]*;)")]
    private static partial Regex StartBlock { get; }

    [GeneratedRegex(@"DoorsIsOpen\s*=\s*0")]
    private static partial Regex DoorsClosed { get; }

    [GeneratedRegex(@"\bLightOff\s*\(\s*(0[xX][0-9a-fA-F]+|\d+)\s*\)")]
    private static partial Regex LightOffCall { get; }

    [GeneratedRegex(@"\biff\s*\(\s*SizeTo\s*\(\s*Flagman\s*\(\s*0\s*\)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*\)\s*<\s*(\d+)\s*\)\s*\{")]
    private static partial Regex DistanceBlock { get; }
}
