using System.Numerics;
using System.Text;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Scripting;

// Reads the if blocks of an Alien Shooter 2 level script and places the ones that change the level where their conditions come true
internal static class LevelScriptBlockReader
{
    private const int ResolvePasses = 3;

    public static void Read(string text, ScriptContext context)
    {
        text = StripComments(text);
        var blocks = new List<Block>();
        ReadBlocks(text, [], blocks);
        var variables = context.VariableSets.ToList();
        for (int pass = 0; pass < ResolvePasses; pass++)
        {
            foreach (var block in blocks)
            {
                foreach (var place in GetPlaces(block.Conditions, context, variables))
                {
                    foreach (var assignment in GetAssignments(block.Text))
                    {
                        int value = int.TryParse(assignment.Value, out int parsed) ? parsed : 1;
                        variables.Add((assignment.Name, value, place.Center, place.Radius));
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

    public static LevelScriptSummary ReadSummary(string text)
    {
        text = StripComments(text);

        return new LevelScriptSummary
        {
            ClearsMonsters = text.Contains("CompaignGameTact()", StringComparison.Ordinal),
            Handlers = ReadSequelHandlers(text)
        };
    }

    private static ScriptHandler[] ReadSequelHandlers(string text)
    {
        var handlers = new List<ScriptHandler>();
        for (int start = 0; start < text.Length; start = text.IndexOf('\n', start) is int line and >= 0 ? line + 1 : text.Length)
        {
            var cursor = new TextCursor(text, start);
            if (!cursor.Read("F") || !cursor.ReadInteger(false, out int id) || !cursor.Read("_") || !cursor.ReadInteger(false, out int state) ||
                state is not (15 or 16) || !cursor.ReadAfterSpace("(") || !cursor.ReadAfterSpace("int") || cursor.Peek() is not (' ' or '\t' or '\r' or '\n'))
                continue;

            cursor.SkipSpace();
            if (!cursor.ReadName(out _) || !cursor.ReadAfterSpace(")") || !cursor.ReadAfterSpace("{"))
                continue;

            int closing = FindClosing(text, cursor.Position - 1, '{', '}', true);
            if (closing < 0)
                continue;

            string body = InlineSequelCalls(text, text[cursor.Position..closing]);
            handlers.Add(new ScriptHandler((KnownObject)id, state == 15 ? SpriteAnimation.Death : SpriteAnimation.Death2, body.Contains("Win()", StringComparison.Ordinal),
                FindGenocides(body).Select(id => (KnownObject)id).ToArray(), FindSequelKills(body)));
        }

        return handlers.ToArray();
    }

    private static string InlineSequelCalls(string text, string body)
    {
        string inlined = body;
        for (int index = 0; index < body.Length; index++)
        {
            var cursor = new TextCursor(body, index);
            if (!cursor.IsWordStart || !char.IsAsciiLetterUpper(cursor.Peek()) || !cursor.ReadName(out var name) || !cursor.ReadAfterSpace("(") ||
                !cursor.ReadAfterSpace(")") || !cursor.ReadAfterSpace(";"))
                continue;

            int opening = FindDefinition(text, name);
            int closing = opening < 0 ? -1 : FindClosing(text, opening, '{', '}', true);
            if (closing >= 0)
            {
                inlined += text[(opening + 1)..closing];
            }
        }

        return inlined;
    }

    private static int FindDefinition(string text, ReadOnlySpan<char> name)
    {
        for (int start = 0; start < text.Length; start = text.IndexOf('\n', start) is int line and >= 0 ? line + 1 : text.Length)
        {
            var cursor = new TextCursor(text, start);
            cursor.SkipSpace();
            if (cursor.Read(name) && cursor.ReadAfterSpace("(") && cursor.ReadAfterSpace(")") && cursor.ReadAfterSpace("{"))
                return cursor.Position - 1;
        }

        return -1;
    }

    private static KnownObject[] FindSequelKills(string body)
    {
        var kills = new List<KnownObject>();
        for (int index = 0; index < body.Length; index++)
        {
            var cursor = new TextCursor(body, index);
            if (cursor.Read("GetUnitVid(") && cursor.ReadAfterSpace("i") && cursor.ReadAfterSpace(")") && cursor.ReadAfterSpace("==") &&
                cursor.ReadIntegerAfterSpace(false, out int vid) && cursor.ReadAfterSpace(")") && cursor.ReadAfterSpace("Action(") && cursor.ReadAfterSpace("i") &&
                cursor.ReadAfterSpace(",") && cursor.ReadAfterSpace("ACT_CHANGE_ANIMATION") && cursor.ReadAfterSpace(",") && cursor.ReadAfterSpace("ANI_DEATH") &&
                cursor.ReadAfterSpace(")"))
            {
                kills.Add((KnownObject)vid);
            }
        }

        return kills.ToArray();
    }

    // Finds the blocks that move the player once a key block has set their variable, and the quest the key block waits for
    private static void ReadSkips(List<Block> blocks, ScriptContext context)
    {
        foreach (var block in blocks)
        {
            if (!TryFindPlayerMove(block.Text, out var destination))
            {
                continue;
            }

            foreach (string name in block.Conditions.Select(FindComparison).OfType<Comparison>().Select(comparison => comparison.Name))
            {
                foreach (var press in blocks.Where(candidate => candidate.Conditions.Any(ContainsKeyPress) && Assigns(candidate, name)))
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

    private static bool Assigns(Block block, string name) => GetAssignments(block.Text).Any(assignment => assignment.Name == name);

    // Follows the variables of the conditions back to the quest that makes them true
    private static (int Line, int Sub)? FindQuest(List<Block> blocks, List<string> conditions, int depth)
    {
        var quest = GetQuest(conditions);
        if (quest is not null || depth == 0)
        {
            return quest;
        }

        foreach (string name in conditions.Select(GetBareName).OfType<string>())
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
            int found = FindIf(text, position, out int length);
            if (found < 0)
            {
                own.Append(text, position, text.Length - position);
                break;
            }

            own.Append(text, position, found - position);
            int open = found + length - 1;
            int close = FindClosing(text, open, '(', ')');
            if (close < 0)
            {
                break;
            }

            int bodyStart = SkipSpace(text, close + 1);
            int bodyEnd = FindStatementEnd(text, bodyStart);
            string body = bodyStart < text.Length && text[bodyStart] == '{' ? text[(bodyStart + 1)..bodyEnd] : text[bodyStart..Math.Min(text.Length, bodyEnd + 1)];
            ReadBlocks(body, conditions.Append(text[(open + 1)..close]).ToList(), blocks);
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

        int nested = FindIf(text, start, out int length);
        if (nested == start)
        {
            int close = FindClosing(text, nested + length - 1, '(', ')');
            return close < 0 ? text.Length - 1 : FindStatementEnd(text, SkipSpace(text, close + 1));
        }

        int semicolon = text.IndexOf(';', start);
        return semicolon < 0 ? text.Length - 1 : semicolon;
    }

    private static int FindClosing(string text, int open, char opening, char closing, bool skipsText = false)
    {
        int depth = 0;
        bool quoted = false;
        for (int index = open; index < text.Length; index++)
        {
            char value = text[index];
            if (skipsText && quoted && value == '\\')
            {
                index++;
                continue;
            }

            if (skipsText && value == '"')
            {
                quoted = !quoted;
            }

            if (quoted)
                continue;

            if (value == opening)
            {
                depth++;
            }
            else if (value == closing && --depth == 0)
                return index;
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
            var area = FindArea(condition);
            if (area is not null)
            {
                return [(new Vector3(area.Value.X, area.Value.Y, 0), area.Value.Radius)];
            }

            var quest = FindQuestState(condition);
            if (quest is not null)
            {
                int line = quest.Value.Line + 1;
                int sub = quest.Value.Sub;
                var starts = context.QuestStarts.Where(start => start.Quest.Line == line && start.Quest.Sub == sub).Select(start => (start.Position, 0f)).ToList();
                if (starts.Count > 0)
                {
                    return starts;
                }

                continue;
            }

            var comparison = FindComparison(condition);
            string? bare = GetBareName(condition);
            if (comparison is null && bare is null)
            {
                continue;
            }

            string name = comparison is not null ? comparison.Value.Name : bare!;

            int? value = comparison?.Value;
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
        for (int index = conditions.Count - 1; index >= 0; index--)
        {
            var quest = FindQuestState(conditions[index]);
            if (quest is not null)
                return (quest.Value.Line + 1, quest.Value.Sub);
        }

        return null;
    }

    private static string? DescribeCondition(string condition)
    {
        var area = FindArea(condition);
        if (area is not null)
        {
            return $"when the player comes within {area.Value.Radius} px";
        }

        var quest = FindQuestState(condition);
        if (quest is not null)
        {
            return $"once quest {quest.Value.Line + 1}_{quest.Value.Sub} starts";
        }

        var comparison = FindComparison(condition);
        if (comparison is not null)
        {
            return $"once {comparison.Value.Name} is {comparison.Value.Value}";
        }

        string? bare = GetBareName(condition);
        return bare is not null ? $"while {bare} is set" : null;
    }

    private static List<(ScriptMarkerKind Kind, string Text)> GetEffects(string text, (int Line, int Sub)? quest, ScriptContext context, out Vector3? target)
    {
        var effects = new List<(ScriptMarkerKind Kind, string Text)>();
        target = null;
        foreach (int id in FindGenocides(text))
        {
            var removed = context.GetSprites((KnownObject)id);
            effects.Add((ScriptMarkerKind.Opening, $"remove every {context.GetObjectName(id)} ×{removed.Count}"));
            foreach (var sprite in removed)
            {
                context.Open(sprite.Handle, quest);
            }
        }

        if (ContainsMissionFailed(text))
        {
            effects.Add((ScriptMarkerKind.Failure, "fail the mission"));
        }

        if (ContainsWin(text))
        {
            effects.Add((ScriptMarkerKind.Exit, "win the level"));
        }

        if (TryFindPlayerMove(text, out var move))
        {
            target = move;
            effects.Add((ScriptMarkerKind.Teleport, $"move the player to ({move.X:0}, {move.Y:0})"));
        }

        if (ContainsDeathTimer(text))
        {
            effects.Add((ScriptMarkerKind.Other, "set a death timer on the player"));
        }

        if (ContainsLight(text))
        {
            effects.Add((ScriptMarkerKind.Other, "change the light"));
        }

        return effects;
    }

    private sealed record Block(List<string> Conditions, string Text);

    private static string StripComments(string text)
    {
        var withoutBlocks = new StringBuilder(text.Length);
        int position = 0;
        while (position < text.Length)
        {
            int open = text.IndexOf("/*", position, StringComparison.Ordinal);
            int close = open < 0 ? -1 : text.IndexOf("*/", open + 2, StringComparison.Ordinal);
            if (close < 0)
            {
                withoutBlocks.Append(text, position, text.Length - position);
                break;
            }

            withoutBlocks.Append(text, position, open - position);
            position = close + 2;
        }

        string code = withoutBlocks.ToString();
        var result = new StringBuilder(code.Length);
        position = 0;
        while (position < code.Length)
        {
            int comment = code.IndexOf("//", position, StringComparison.Ordinal);
            if (comment < 0)
            {
                result.Append(code, position, code.Length - position);
                break;
            }

            result.Append(code, position, comment - position);
            int end = code.IndexOf('\n', comment);
            position = end < 0 ? code.Length : end;
        }

        return result.ToString();
    }

    private static int FindIf(string text, int start, out int length)
    {
        for (int index = start; index < text.Length; index++)
        {
            var cursor = new TextCursor(text, index);
            if (cursor.IsWordStart && (MatchOpening(ref cursor, "iff") || MatchOpening(ref cursor, "if")))
            {
                length = cursor.Position - index;
                return index;
            }
        }

        length = 0;
        return -1;
    }

    private static bool MatchOpening(ref TextCursor cursor, string keyword)
    {
        int start = cursor.Position;
        if (cursor.Read(keyword) && cursor.ReadAfterSpace("("))
            return true;

        cursor = new TextCursor(cursor.Text, start);
        return false;
    }

    private static Area? FindArea(string text)
    {
        for (int index = 0; index < text.Length; index++)
        {
            var cursor = new TextCursor(text, index);
            if (cursor.Read("SizeTo(") && cursor.ReadAfterSpace("Flagman(0)") && cursor.ReadAfterSpace(",") && cursor.ReadIntegerAfterSpace(true, out int x) &&
                cursor.ReadAfterSpace(",") && cursor.ReadIntegerAfterSpace(true, out int y) && cursor.ReadAfterSpace(")") && cursor.ReadAfterSpace("<") &&
                cursor.ReadIntegerAfterSpace(false, out int radius))
                return new Area(x, y, radius);
        }

        return null;
    }

    private static (int Line, int Sub)? FindQuestState(string text)
    {
        for (int index = 0; index < text.Length; index++)
        {
            var cursor = new TextCursor(text, index);
            if (cursor.Read("EXT_GETQUESTSSTATE") && cursor.ReadAfterSpace(",") && cursor.ReadIntegerAfterSpace(false, out int line) && cursor.ReadAfterSpace(",") &&
                cursor.ReadIntegerAfterSpace(false, out int sub))
                return (line, sub);
        }

        return null;
    }

    private static Comparison? FindComparison(string text)
    {
        for (int index = 0; index < text.Length; index++)
        {
            var cursor = new TextCursor(text, index);
            if (cursor.IsWordStart && cursor.ReadName(out var name) && cursor.ReadAfterSpace("==") && cursor.ReadIntegerAfterSpace(false, out int value))
                return new Comparison(name.ToString(), value);
        }

        return null;
    }

    private static string? GetBareName(string text)
    {
        var cursor = new TextCursor(text, 0);
        cursor.SkipSpace();
        if (!cursor.ReadName(out var name))
            return null;

        cursor.SkipSpace();
        return cursor.IsAtEnd || cursor.Read("&&") ? name.ToString() : null;
    }

    private static List<(string Name, string Value)> GetAssignments(string text)
    {
        var assignments = new List<(string Name, string Value)>();
        int index = 0;
        while (index < text.Length)
        {
            var cursor = new TextCursor(text, index);
            if (!cursor.IsWordStart || !cursor.ReadName(out var name) || !cursor.ReadAfterSpace("=") || cursor.Peek() == '=')
            {
                index++;
                continue;
            }

            int start = cursor.Position;
            int end = text.IndexOf(';', start);
            cursor.SkipSpace();
            if (end < 0 || end == start)
            {
                index++;
                continue;
            }

            int valueStart = cursor.Position < end ? cursor.Position : end - 1;
            assignments.Add((name.ToString(), text[valueStart..end]));
            index = end + 1;
        }

        return assignments;
    }

    private static bool ContainsKeyPress(string text) => Contains(text, static (ref TextCursor cursor) =>
        cursor.Read("GetKey()") && cursor.ReadAfterSpace("==") && cursor.ReadAfterSpace("' '"));

    private static List<int> FindGenocides(string text)
    {
        var ids = new List<int>();
        for (int index = 0; index < text.Length; index++)
        {
            var cursor = new TextCursor(text, index);
            if (cursor.IsWordStart && cursor.Read("Genocide(") && cursor.ReadIntegerAfterSpace(false, out int id) && cursor.ReadAfterSpace(")"))
            {
                ids.Add(id);
                index = cursor.Position - 1;
            }
        }

        return ids;
    }

    private static bool ContainsMissionFailed(string text) =>
        text.Contains("GAME_MISSION_FAILED", StringComparison.Ordinal) || ContainsCall(text, "FailCurQuest(");

    private static bool ContainsWin(string text) => ContainsCall(text, "Win(") || ContainsCall(text, "InstantWin(") || ContainsCall(text, "GoToNextMap(");

    private static bool ContainsLight(string text) => ContainsCall(text, "ChangeLight(") || ContainsCall(text, "LightOff(") || ContainsCall(text, "SetGamma(");

    private static bool ContainsDeathTimer(string text) => Contains(text, static (ref TextCursor cursor) =>
        cursor.Read("Flagman(0)") && cursor.ReadAfterSpace(",") && cursor.ReadAfterSpace("ACT_SET_DEATH_TIMER"));

    private static bool TryFindPlayerMove(string text, out Vector3 destination)
    {
        for (int index = 0; index < text.Length; index++)
        {
            var cursor = new TextCursor(text, index);
            if (cursor.Read("Action(") && cursor.ReadAfterSpace("Flagman(0)") && cursor.ReadAfterSpace(",") && cursor.ReadAfterSpace("ACT_CHANGE_COOR") &&
                cursor.ReadAfterSpace(",") && cursor.ReadIntegerAfterSpace(true, out int x) && cursor.ReadAfterSpace(",") && cursor.ReadIntegerAfterSpace(true, out int y) &&
                cursor.ReadAfterSpace(",") && cursor.ReadIntegerAfterSpace(true, out int z))
            {
                destination = new Vector3(x, y, z);
                return true;
            }
        }

        destination = default;
        return false;
    }

    private static bool ContainsCall(string text, string call)
    {
        for (int index = text.IndexOf(call, StringComparison.Ordinal); index >= 0; index = text.IndexOf(call, index + 1, StringComparison.Ordinal))
        {
            if (index == 0 || !TextCursor.IsWord(text[index - 1]))
                return true;
        }

        return false;
    }

    private delegate bool CursorMatch(ref TextCursor cursor);

    private static bool Contains(string text, CursorMatch match)
    {
        for (int index = 0; index < text.Length; index++)
        {
            var cursor = new TextCursor(text, index);
            if (match(ref cursor))
                return true;
        }

        return false;
    }

    private readonly record struct Area(int X, int Y, int Radius);

    private readonly record struct Comparison(string Name, int Value);
}
