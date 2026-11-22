using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Scripting;

/// <summary>
/// Provides methods for reading the script events of a level from its map and its level script.
/// </summary>
public static class LevelLogicReader
{
    private const int ResolvePasses = 3;

    /// <summary>
    /// Reads the script events of a level and the order its quests lead the player through.
    /// </summary>
    /// <param name="mapPath">The path of the map file. The level script is the .lgc file of the same name next to it.</param>
    /// <param name="map">The map.</param>
    /// <param name="objects">The object table of the game.</param>
    /// <param name="quests">The quests of the game.</param>
    /// <returns>The script events and the quest order of the level.</returns>
    public static LevelLogic Read(string mapPath, MapResourceBuilder map, ObjectTable objects, QuestBook quests)
    {
        var context = new ScriptContext(map, objects, quests);
        string? scriptPath = FindLevelScript(mapPath);
        var script = scriptPath is null ? new LevelScriptSummary() : LevelScriptReader.Read(scriptPath, objects.ScriptVersion);
        if (!objects.IsAlienShooter2)
        {
            AddLevelScriptMarkers(context.Markers, script);
        }

        foreach (var sprite in map.Sprites)
        {
            if (!SpriteProgram.TryParse(sprite.Payload, objects.IsAlienShooter2, out var program))
            {
                continue;
            }

            var reader = new StackReader(sprite, program, context);
            for (int stack = 0; stack < program.StackCount; stack++)
            {
                reader.Read(program.GetStack(stack));
            }
        }

        AddScriptBlocks(context, script.Blocks);

        foreach (var start in context.VariableQuestStarts)
        {
            foreach (var set in context.GetVariableSetters(start.Name, start.Value))
            {
                context.QuestStarts.Add((start.Quest, set.Position, set.Radius));
            }
        }

        var triggers = GetOpeningTriggers(context);
        var skips = context.Skips.Select(skip => new LevelSkip(context.GetBeginTimeLine(skip.Quest), skip.Destination,
            $"press space to move the player to ({skip.Destination.X:0}, {skip.Destination.Y:0})")).ToList();
        var progression = ProgressionReader.Read(context);
        var openings = context.Openings.ToDictionary(pair => pair.Key,
            pair => pair.Value.Min(quest => quest is null ? 0 : context.GetBeginTimeLine(quest.Value)));
        return new LevelLogic
        {
            Markers = context.Markers,
            Progression = progression,
            Openings = openings,
            Triggers = triggers,
            Detours = ProgressionReader.ReadDetours(context, progression),
            Skips = skips,
            Controls = context.Controls
        };
    }

    // Pairs the sprites that a variable wait opens with the triggers that set the variable
    private static List<OpeningTrigger> GetOpeningTriggers(ScriptContext context)
    {
        var triggers = new List<OpeningTrigger>();
        foreach (var group in context.VariableOpenings.GroupBy(opening => (opening.Name, opening.Value)))
        {
            var setters = context.GetVariableSetters(group.Key.Name, group.Key.Value).ToList();
            int[] handles = group.Select(opening => opening.Handle).Distinct().Where(context.IsWall).ToArray();
            foreach (var opening in group.Where(opening => setters.Count == 0 || !handles.Contains(opening.Handle)))
            {
                context.Open(opening.Handle, null);
            }

            if (setters.Count == 0 || handles.Length == 0)
            {
                continue;
            }

            triggers.AddRange(setters.Select(setter => new OpeningTrigger(setter.Position, setter.Radius, context.GetOpeningLabel(handles[0]), handles)));
        }

        foreach (var group in context.AreaOpenings.GroupBy(opening => (opening.Center, opening.Radius, opening.Quest)))
        {
            int[] handles = group.Select(opening => opening.Handle).Distinct().ToArray();
            int timeLine = group.Key.Quest is null ? 0 : context.GetBeginTimeLine(group.Key.Quest.Value);
            triggers.Add(new OpeningTrigger(group.Key.Center, group.Key.Radius, context.GetOpeningLabel(handles[0]), handles, timeLine, true));
        }

        return triggers;
    }

    /// <summary>
    /// Finds the level script of a map.
    /// </summary>
    /// <param name="mapPath">The path of the map file.</param>
    /// <returns>The path of the level script, or null if there isn't one next to the map.</returns>
    public static string? FindLevelScript(string mapPath)
    {
        string? folder = Path.GetDirectoryName(mapPath);
        return folder is null ? null : GamePath.Resolve(folder, Path.GetFileNameWithoutExtension(mapPath) + ".lgc");
    }

    private static void AddScriptBlocks(ScriptContext context, IReadOnlyList<ScriptBlock> blocks)
    {
        var variables = context.VariableSets.ToList();
        for (int pass = 0; pass < ResolvePasses; pass++)
        {
            foreach (var block in blocks)
            {
                foreach (var place in GetPlaces(context, block.Conditions, variables))
                {
                    foreach (var assignment in block.Assignments)
                    {
                        variables.Add((assignment.Variable, assignment.Value, place.Center, place.Radius));
                    }
                }
            }

            variables = variables.Distinct().ToList();
        }

        AddSkips(context, blocks);
        foreach (var block in blocks)
        {
            var quest = GetQuest(block.Conditions);
            var effects = GetEffects(context, block, quest);
            if (effects.Count == 0)
                continue;

            var kind = effects.Max(effect => effect.Kind);
            string conditions = string.Join(", ", block.Conditions.Select(DescribeCondition).OfType<string>());
            string label = $"level script{(conditions.Length > 0 ? ", " + conditions : "")}: {string.Join("; ", effects.Select(effect => effect.Text).Distinct())}";
            foreach (var place in GetPlaces(context, block.Conditions, variables).DistinctBy(place => place.Center))
            {
                if (context.Markers.Any(marker => marker.Label == label && marker.Position == place.Center))
                    continue;

                context.Markers.Add(new ScriptMarker(kind, place.Center, place.Radius, place.Center, label) { Target = block.PlayerMove });
                if (kind == ScriptMarkerKind.Exit)
                {
                    context.Exits.Add((place.Center, place.Radius, quest, label));
                }
            }
        }
    }

    private static void AddSkips(ScriptContext context, IReadOnlyList<ScriptBlock> blocks)
    {
        foreach (var block in blocks)
        {
            if (block.PlayerMove is null)
                continue;

            var destination = block.PlayerMove.Value;
            foreach (var condition in block.Conditions)
            {
                string? name = condition.Variable;
                if (name is null)
                    continue;

                foreach (var press in blocks.Where(candidate => candidate.Conditions.Any(press => press.WaitForSpace) && Assigns(candidate, name)))
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

    private static bool Assigns(ScriptBlock block, string name) => block.Assignments.Any(assignment => assignment.Variable == name);

    private static (int Line, int Sub)? FindQuest(IReadOnlyList<ScriptBlock> blocks, IReadOnlyList<ScriptCondition> conditions, int depth)
    {
        var quest = GetQuest(conditions);
        if (quest is not null || depth == 0)
            return quest;

        foreach (var condition in conditions)
        {
            string? name = condition.Flag;
            if (name is null)
                continue;

            foreach (var setter in blocks.Where(candidate => Assigns(candidate, name)))
            {
                quest = FindQuest(blocks, setter.Conditions, depth - 1);
                if (quest is not null)
                    return quest;
            }
        }

        return null;
    }

    private static List<(Vector3 Center, float Radius)> GetPlaces(ScriptContext context, IReadOnlyList<ScriptCondition> conditions,
        List<(string Name, int Value, Vector3 Position, float Radius)> variables)
    {
        for (int i = conditions.Count - 1; i >= 0; i--)
        {
            var condition = conditions[i];
            if (condition.Area is not null)
            {
                var area = condition.Area.Value;
                return [(new Vector3(area.Center.X, area.Center.Y, 0), area.Radius)];
            }

            if (condition.Quest is not null)
            {
                var quest = condition.Quest.Value;
                var starts = context.QuestStarts.Where(start => start.Quest.Line == quest.Line && start.Quest.Sub == quest.Sub).Select(start => (start.Position, 0f)).ToList();
                if (starts.Count > 0)
                    return starts;

                continue;
            }

            string? name = condition.Variable ?? condition.Flag;
            if (name is null)
                continue;

            int? value = condition.Variable is not null ? condition.Value : null;
            var sets = variables.Where(set => set.Name == name && (value is null ? set.Value != 0 : set.Value == value)).Select(set => (set.Position, 0f)).ToList();
            if (sets.Count > 0)
                return sets;
        }

        return [];
    }

    private static (int Line, int Sub)? GetQuest(IReadOnlyList<ScriptCondition> conditions)
    {
        for (int i = conditions.Count - 1; i >= 0; i--)
        {
            if (conditions[i].Quest is not null)
                return conditions[i].Quest;
        }

        return null;
    }

    private static string? DescribeCondition(ScriptCondition condition)
    {
        if (condition.Area is not null)
            return $"when the player comes within {condition.Area.Value.Radius} px";

        if (condition.Quest is not null)
            return $"once quest {condition.Quest.Value.Line}_{condition.Quest.Value.Sub} starts";

        if (condition.Variable is not null)
            return $"once {condition.Variable} is {condition.Value}";

        return condition.Flag is not null ? $"while {condition.Flag} is set" : null;
    }

    private static List<(ScriptMarkerKind Kind, string Text)> GetEffects(ScriptContext context, ScriptBlock block, (int Line, int Sub)? quest)
    {
        var effects = new List<(ScriptMarkerKind Kind, string Text)>();
        foreach (var id in block.Removals)
        {
            var removed = context.GetSprites(id);
            effects.Add((ScriptMarkerKind.Opening, $"remove every {context.GetObjectName((int)id)} ×{removed.Count}"));
            foreach (var sprite in removed)
            {
                context.Open(sprite.Handle, quest);
            }
        }

        if (block.LosesLevel)
        {
            effects.Add((ScriptMarkerKind.Failure, "lose the level"));
        }

        if (block.WinsLevel)
        {
            effects.Add((ScriptMarkerKind.Exit, "win the level"));
        }

        if (block.PlayerMove is not null)
        {
            var move = block.PlayerMove.Value;
            effects.Add((ScriptMarkerKind.Teleport, $"move the player to ({move.X:0}, {move.Y:0})"));
        }

        if (block.SetsDeathTimer)
        {
            effects.Add((ScriptMarkerKind.Other, "set a death timer on the player"));
        }

        if (block.ChangesLight)
        {
            effects.Add((ScriptMarkerKind.Other, "change the light"));
        }

        return effects;
    }

    private static void AddLevelScriptMarkers(List<ScriptMarker> markers, LevelScriptSummary script)
    {
        if (script.Blackout is not null)
        {
            markers.Add(CreateAreaMarker(ScriptMarkerKind.Blackout, script.Blackout.Value, "level script: turn the lights off and lock the doors"));
        }

        foreach (var checkpoint in script.Checkpoints)
        {
            markers.Add(CreateAreaMarker(ScriptMarkerKind.Checkpoint, checkpoint, "level script: set the respawn point"));
        }

        if (script.Exit is not null)
        {
            markers.Add(CreateAreaMarker(ScriptMarkerKind.Exit, script.Exit.Value, "level script: win the level"));
        }
    }

    private static ScriptMarker CreateAreaMarker(ScriptMarkerKind kind, TriggerArea area, string label)
    {
        var center = new Vector3(area.Center.X, area.Center.Y, 0);
        return new ScriptMarker(kind, center, area.Radius, center, label);
    }
}
