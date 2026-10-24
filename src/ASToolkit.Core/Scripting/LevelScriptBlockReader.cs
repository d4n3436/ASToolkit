using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Scripting;

// Reads the if blocks of an Alien Shooter 2 level script and places the ones that change the level where their conditions come true
internal static partial class LevelScriptBlockReader
{
    private const int ResolvePasses = 3;

    public static void Read(string text, ScriptContext context)
    {
        text = LineComment.Replace(BlockComment.Replace(text, ""), "");
        var blocks = new List<Block>();
        ReadBlocks(text, [], blocks);
        var variables = context.VariableSets.ToList();
        for (int pass = 0; pass < ResolvePasses; pass++)
        {
            foreach (var block in blocks)
            {
                foreach (var place in GetPlaces(block.Conditions, context, variables))
                {
                    foreach (Match assignment in Assignment.Matches(block.Text))
                    {
                        int value = int.TryParse(assignment.Groups[2].ValueSpan, out int parsed) ? parsed : 1;
                        variables.Add((assignment.Groups[1].Value, value, place.Center, place.Radius));
                    }
                }
            }

            variables = variables.Distinct().ToList();
        }

        ReadSkips(blocks, context);
        foreach (var block in blocks)
        {
            var effects = GetEffects(block.Text, GetQuest(block.Conditions), context, out var target);
            if (effects.Count == 0)
            {
                continue;
            }

            var kind = effects.Max(effect => effect.Kind);
            string conditions = string.Join(", ", block.Conditions.Select(DescribeCondition).OfType<string>());
            string label = $"level script{(conditions.Length > 0 ? ", " + conditions : "")}: {string.Join("; ", effects.Select(effect => effect.Text).Distinct())}";
            foreach (var place in GetPlaces(block.Conditions, context, variables).DistinctBy(place => place.Center))
            {
                if (context.Markers.Any(marker => marker.Label == label && marker.Position == place.Center))
                {
                    continue;
                }

                context.Markers.Add(new ScriptMarker(kind, place.Center, place.Radius, place.Center, label) { Target = target });
                if (kind == ScriptMarkerKind.Exit)
                {
                    context.Exits.Add((place.Center, place.Radius, GetQuest(block.Conditions), label));
                }
            }
        }
    }

    // Finds the blocks that move the player once a key block has set their variable, and the quest the key block waits for
    private static void ReadSkips(List<Block> blocks, ScriptContext context)
    {
        foreach (var block in blocks)
        {
            var move = PlayerMove.Match(block.Text);
            if (!move.Success)
            {
                continue;
            }

            var destination = new Vector3(int.Parse(move.Groups[1].ValueSpan), int.Parse(move.Groups[2].ValueSpan), int.Parse(move.Groups[3].ValueSpan));
            foreach (string name in block.Conditions.Select(condition => Comparison.Match(condition)).Where(match => match.Success).Select(match => match.Groups[1].Value))
            {
                foreach (var press in blocks.Where(candidate => candidate.Conditions.Any(condition => KeyPress.IsMatch(condition)) && Assigns(candidate, name)))
                {
                    var quest = FindQuest(blocks, press.Conditions, ResolvePasses);
                    if (quest is not null && !context.Skips.Any(skip => skip.Quest == quest.Value && skip.Destination == destination))
                    {
                        context.Skips.Add((quest.Value, destination));
                    }
                }
            }
        }
    }

    private static bool Assigns(Block block, string name) => Assignment.Matches(block.Text).Any(match => match.Groups[1].Value == name);

    // Follows the variables of the conditions back to the quest that makes them true
    private static (int Line, int Sub)? FindQuest(List<Block> blocks, List<string> conditions, int depth)
    {
        var quest = GetQuest(conditions);
        if (quest is not null || depth == 0)
        {
            return quest;
        }

        foreach (string name in conditions.Select(condition => BareName.Match(condition)).Where(match => match.Success).Select(match => match.Groups[1].Value))
        {
            foreach (var setter in blocks.Where(candidate => Assigns(candidate, name)))
            {
                quest = FindQuest(blocks, setter.Conditions, depth - 1);
                if (quest is not null)
                {
                    return quest;
                }
            }
        }

        return null;
    }

    private static void ReadBlocks(string text, List<string> conditions, List<Block> blocks)
    {
        var own = new StringBuilder();
        int position = 0;
        while (position < text.Length)
        {
            var match = IfStart.Match(text, position);
            if (!match.Success)
            {
                own.Append(text, position, text.Length - position);
                break;
            }

            own.Append(text, position, match.Index - position);
            int open = match.Index + match.Length - 1;
            int close = FindClosing(text, open, '(', ')');
            if (close < 0)
            {
                break;
            }

            int bodyStart = SkipSpace(text, close + 1);
            int bodyEnd = FindStatementEnd(text, bodyStart);
            string body = bodyStart < text.Length && text[bodyStart] == '{' ? text[(bodyStart + 1)..bodyEnd] : text[bodyStart..Math.Min(text.Length, bodyEnd + 1)];
            ReadBlocks(body, [.. conditions, text[(open + 1)..close]], blocks);
            position = bodyEnd + 1;
        }

        blocks.Add(new Block(conditions, own.ToString()));
    }

    private static int FindStatementEnd(string text, int start)
    {
        if (start >= text.Length)
        {
            return text.Length - 1;
        }

        if (text[start] == '{')
        {
            int end = FindClosing(text, start, '{', '}');
            return end < 0 ? text.Length - 1 : end;
        }

        var nested = IfStart.Match(text, start);
        if (nested.Success && nested.Index == start)
        {
            int close = FindClosing(text, nested.Index + nested.Length - 1, '(', ')');
            return close < 0 ? text.Length - 1 : FindStatementEnd(text, SkipSpace(text, close + 1));
        }

        int semicolon = text.IndexOf(';', start);
        return semicolon < 0 ? text.Length - 1 : semicolon;
    }

    private static int FindClosing(string text, int open, char opening, char closing)
    {
        int depth = 0;
        for (int index = open; index < text.Length; index++)
        {
            if (text[index] == opening)
            {
                depth++;
            }
            else if (text[index] == closing && --depth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static int SkipSpace(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index;
    }

    private static List<(Vector3 Center, float Radius)> GetPlaces(List<string> conditions, ScriptContext context,
        List<(string Name, int Value, Vector3 Position, float Radius)> variables)
    {
        for (int index = conditions.Count - 1; index >= 0; index--)
        {
            string condition = conditions[index];
            var area = Area.Match(condition);
            if (area.Success)
            {
                return [(new Vector3(int.Parse(area.Groups[1].ValueSpan), int.Parse(area.Groups[2].ValueSpan), 0), int.Parse(area.Groups[3].ValueSpan))];
            }

            var quest = QuestState.Match(condition);
            if (quest.Success)
            {
                int line = int.Parse(quest.Groups[1].ValueSpan) + 1;
                int sub = int.Parse(quest.Groups[2].ValueSpan);
                var starts = context.QuestStarts.Where(start => start.Quest.Line == line && start.Quest.Sub == sub).Select(start => (start.Position, 0f)).ToList();
                if (starts.Count > 0)
                {
                    return starts;
                }

                continue;
            }

            var comparison = Comparison.Match(condition);
            var bare = BareName.Match(condition);
            if (!comparison.Success && !bare.Success)
            {
                continue;
            }

            string name = comparison.Success ? comparison.Groups[1].Value : bare.Groups[1].Value;

            int? value = comparison.Success ? int.Parse(comparison.Groups[2].ValueSpan) : null;
            var sets = variables.Where(set => set.Name == name && (value is null ? set.Value != 0 : set.Value == value)).Select(set => (set.Position, 0f)).ToList();
            if (sets.Count > 0)
            {
                return sets;
            }
        }

        return [];
    }

    private static (int Line, int Sub)? GetQuest(List<string> conditions)
    {
        var quest = conditions.Select(condition => QuestState.Match(condition)).LastOrDefault(match => match.Success);
        return quest is null ? null : (int.Parse(quest.Groups[1].ValueSpan) + 1, int.Parse(quest.Groups[2].ValueSpan));
    }

    private static string? DescribeCondition(string condition)
    {
        var area = Area.Match(condition);
        if (area.Success)
        {
            return $"when the player comes within {area.Groups[3].Value} px";
        }

        var quest = QuestState.Match(condition);
        if (quest.Success)
        {
            return $"once quest {int.Parse(quest.Groups[1].ValueSpan) + 1}_{quest.Groups[2].Value} starts";
        }

        var comparison = Comparison.Match(condition);
        if (comparison.Success)
        {
            return $"once {comparison.Groups[1].Value} is {comparison.Groups[2].Value}";
        }

        var bare = BareName.Match(condition);
        return bare.Success ? $"while {bare.Groups[1].Value} is set" : null;
    }

    private static List<(ScriptMarkerKind Kind, string Text)> GetEffects(string text, (int Line, int Sub)? quest, ScriptContext context, out Vector3? target)
    {
        var effects = new List<(ScriptMarkerKind Kind, string Text)>();
        target = null;
        foreach (Match genocide in Genocide.Matches(text))
        {
            int id = int.Parse(genocide.Groups[1].ValueSpan);
            var removed = context.GetSprites((KnownObject)id);
            effects.Add((ScriptMarkerKind.Opening, $"remove every {context.GetObjectName(id)} ×{removed.Count}"));
            foreach (var sprite in removed)
            {
                context.Open(sprite.Handle, quest);
            }
        }

        if (MissionFailed.IsMatch(text))
        {
            effects.Add((ScriptMarkerKind.Failure, "fail the mission"));
        }

        if (Win.IsMatch(text))
        {
            effects.Add((ScriptMarkerKind.Exit, "win the level"));
        }

        var move = PlayerMove.Match(text);
        if (move.Success)
        {
            target = new Vector3(int.Parse(move.Groups[1].ValueSpan), int.Parse(move.Groups[2].ValueSpan), int.Parse(move.Groups[3].ValueSpan));
            effects.Add((ScriptMarkerKind.Teleport, $"move the player to ({move.Groups[1].Value}, {move.Groups[2].Value})"));
        }

        if (DeathTimer.IsMatch(text))
        {
            effects.Add((ScriptMarkerKind.Other, "set a death timer on the player"));
        }

        if (Light.IsMatch(text))
        {
            effects.Add((ScriptMarkerKind.Other, "change the light"));
        }

        return effects;
    }

    private sealed record Block(List<string> Conditions, string Text);

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockComment { get; }

    [GeneratedRegex(@"//[^\n]*")]
    private static partial Regex LineComment { get; }

    [GeneratedRegex(@"\b(?:iff|if)\s*\(")]
    private static partial Regex IfStart { get; }

    [GeneratedRegex(@"SizeTo\(\s*Flagman\(0\)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*\)\s*<\s*(\d+)")]
    private static partial Regex Area { get; }

    [GeneratedRegex(@"EXT_GETQUESTSSTATE\s*,\s*(\d+)\s*,\s*(\d+)")]
    private static partial Regex QuestState { get; }

    [GeneratedRegex(@"\b([A-Za-z_]\w*)\s*==\s*(\d+)")]
    private static partial Regex Comparison { get; }

    [GeneratedRegex(@"^\s*([A-Za-z_]\w*)\s*(?:&&.*)?$", RegexOptions.Singleline)]
    private static partial Regex BareName { get; }

    [GeneratedRegex(@"\b([A-Za-z_]\w*)\s*=\s*(?!=)\s*([^;]+);")]
    private static partial Regex Assignment { get; }

    [GeneratedRegex(@"GetKey\(\)\s*==\s*' '")]
    private static partial Regex KeyPress { get; }

    [GeneratedRegex(@"\bGenocide\(\s*(\d+)\s*\)")]
    private static partial Regex Genocide { get; }

    [GeneratedRegex(@"GAME_MISSION_FAILED|\bFailCurQuest\(")]
    private static partial Regex MissionFailed { get; }

    [GeneratedRegex(@"\b(?:Win|InstantWin|GoToNextMap)\(")]
    private static partial Regex Win { get; }

    [GeneratedRegex(@"Action\(\s*Flagman\(0\)\s*,\s*ACT_CHANGE_COOR\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)")]
    private static partial Regex PlayerMove { get; }

    [GeneratedRegex(@"Flagman\(0\)\s*,\s*ACT_SET_DEATH_TIMER")]
    private static partial Regex DeathTimer { get; }

    [GeneratedRegex(@"\b(?:ChangeLight|LightOff|SetGamma)\(")]
    private static partial Regex Light { get; }
}
