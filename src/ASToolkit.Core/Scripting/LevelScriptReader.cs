using System.Numerics;
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
        text = ReplaceDefines(text);
        var triggers = new List<ScriptTrigger>();
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

            triggers.Add(ReadTrigger(area, body, lightOff.Success ? blackoutTint : null));
        }

        return new LevelScriptSummary
        {
            ClearsMonsters = text.Contains("CompaignGameTact()", StringComparison.Ordinal),
            DoorsLocked = start.Success && DoorsClosed.IsMatch(start.Groups[1].ValueSpan),
            Blackout = blackout,
            BlackoutTint = blackoutTint,
            Checkpoints = checkpoints,
            WinsAfterSites = text.Contains($"GetVidData({(int)KnownObject.DynamiteSite}", StringComparison.Ordinal),
            Exit = exit,
            Triggers = triggers,
            Handlers = ReadHandlers(text),
            CrowdRules = ReadCrowdRules(text)
        };
    }

    private static ScriptCrowdRule[] ReadCrowdRules(string text)
    {
        var rules = new List<ScriptCrowdRule>();
        foreach (Match match in CountCheck.Matches(text))
        {
            int closing = FindBlockEnd(text, match.Index + match.Length - 1);
            if (closing < 0)
                continue;

            var created = SpriteCreation.Matches(text[(match.Index + match.Length)..closing]);
            if (created.Count == 0 || !text.AsSpan(closing + 1).TrimStart().StartsWith("else", StringComparison.Ordinal))
                continue;

            var points = created.Select(call => new Vector3(int.Parse(call.Groups[2].ValueSpan), int.Parse(call.Groups[3].ValueSpan), int.Parse(call.Groups[4].ValueSpan))).ToArray();
            rules.Add(new ScriptCrowdRule((KnownObject)int.Parse(match.Groups[1].ValueSpan), int.Parse(match.Groups[2].ValueSpan),
                (KnownObject)int.Parse(created[0].Groups[1].ValueSpan), points));
        }

        return rules.ToArray();
    }

    private static ScriptTrigger ReadTrigger(TriggerArea area, string body, uint? lightOff)
    {
        var fixedX = FixedRespawnX.Match(body);
        var fixedY = FixedRespawnY.Match(body);
        var respawn = fixedX.Success && fixedY.Success ? ScriptRespawn.Fixed : PlayerRespawn.IsMatch(body) ? ScriptRespawn.Player : ScriptRespawn.None;
        var point = respawn == ScriptRespawn.Fixed ? new Point(int.Parse(fixedX.Groups[1].ValueSpan), int.Parse(fixedY.Groups[1].ValueSpan)) : Point.Empty;
        var quake = QuakeCall.Match(body);

        return new ScriptTrigger(area, respawn, point, DoorsClosed.IsMatch(body), lightOff, quake.Success ? int.Parse(quake.Groups[1].ValueSpan) : 0,
            KeypadSwap.IsMatch(body), body.Contains("Win()", StringComparison.Ordinal), ReadKills(body));
    }

    private static ScriptHandler[] ReadHandlers(string text)
    {
        var handlers = new List<ScriptHandler>();
        foreach (Match match in DeathHandler.Matches(text))
        {
            int closing = FindBlockEnd(text, match.Index + match.Length - 1);
            if (closing < 0)
                continue;

            string body = InlineCalls(text, text[(match.Index + match.Length)..closing]);
            var destroys = Genocide.Matches(body).Select(call => (KnownObject)int.Parse(call.Groups[1].ValueSpan)).ToArray();
            var state = match.Groups[2].ValueSpan is "15" ? SpriteAnimation.Death : SpriteAnimation.Death2;
            handlers.Add(new ScriptHandler((KnownObject)int.Parse(match.Groups[1].ValueSpan), state, body.Contains("Win()", StringComparison.Ordinal), destroys,
                ReadKills(body)));
        }

        return handlers.ToArray();
    }

    // Inline the bodies of functions the level script itself defines
    private static string InlineCalls(string text, string body)
    {
        foreach (Match call in LocalCall.Matches(body))
        {
            var definition = new Regex($@"(?m)^\s*{Regex.Escape(call.Groups[1].Value)}\s*\(\s*\)\s*\{{").Match(text);
            if (!definition.Success)
                continue;

            int closing = FindBlockEnd(text, definition.Index + definition.Length - 1);
            if (closing >= 0)
            {
                body += text[(definition.Index + definition.Length)..closing];
            }
        }

        return body;
    }

    private static KnownObject[] ReadKills(string body) => KillLoop.Matches(body).Select(kill => (KnownObject)int.Parse(kill.Groups[1].ValueSpan)).ToArray();

    private static string ReplaceDefines(string text)
    {
        foreach (Match define in NumericDefine.Matches(text))
        {
            text = Regex.Replace(text, $@"\b{Regex.Escape(define.Groups[1].Value)}\b(?!\s+-?\d)", define.Groups[2].Value);
        }

        return text;
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

    [GeneratedRegex(@"\bif\s*\(\s*GetVidData\s*\(\s*(\d+)\s*,\s*VID_COUNT\s*\)\s*>\s*(\d+)\s*\)\s*\{")]
    private static partial Regex CountCheck { get; }

    [GeneratedRegex(@"\bCreateSprite\s*\(\s*(\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*\)")]
    private static partial Regex SpriteCreation { get; }

    [GeneratedRegex(@"StartPlayerX\s*=\s*(-?\d+)\s*;")]
    private static partial Regex FixedRespawnX { get; }

    [GeneratedRegex(@"StartPlayerY\s*=\s*(-?\d+)\s*;")]
    private static partial Regex FixedRespawnY { get; }

    [GeneratedRegex(@"StartPlayerX\s*=\s*GetX\s*\(\s*Flagman")]
    private static partial Regex PlayerRespawn { get; }

    [GeneratedRegex(@"\bEarthQuake\s*\(\s*(\d+)\s*\)")]
    private static partial Regex QuakeCall { get; }

    [GeneratedRegex(@"GetUnitVid\(\s*i\s*\)\s*==\s*423\s*\)\s*\{\s*CreateSprite\(\s*424")]
    private static partial Regex KeypadSwap { get; }

    [GeneratedRegex(@"GetUnitVid\(\s*i\s*\)\s*==\s*(\d+)\s*\)\s*Action\(\s*i\s*,\s*ACT_CHANGE_ANIMATION\s*,\s*ANI_DEATH\s*\)")]
    private static partial Regex KillLoop { get; }

    [GeneratedRegex(@"\bGenocide\(\s*(\d+)\s*\)")]
    private static partial Regex Genocide { get; }

    [GeneratedRegex(@"(?m)^F(\d+)_(15|16)\s*\(\s*int\s+\w+\s*\)\s*\{")]
    private static partial Regex DeathHandler { get; }

    [GeneratedRegex(@"\b([A-Z]\w*)\s*\(\s*\)\s*;")]
    private static partial Regex LocalCall { get; }

    [GeneratedRegex(@"(?m)^\s*#define\s+(\w+)\s+(-?\d+)\b")]
    private static partial Regex NumericDefine { get; }

    [GeneratedRegex(@"\bLightOff\s*\(\s*(0[xX][0-9a-fA-F]+|\d+)\s*\)")]
    private static partial Regex LightOffCall { get; }

    [GeneratedRegex(@"\biff\s*\(\s*SizeTo\s*\(\s*Flagman\s*\(\s*0\s*\)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*\)\s*<\s*(\d+)\s*\)\s*\{")]
    private static partial Regex DistanceBlock { get; }
}
