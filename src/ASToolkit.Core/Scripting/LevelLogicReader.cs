using System.Numerics;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Scripting;

/// <summary>
/// Provides the reading of the script events of a level from its map and its level script.
/// </summary>
public static class LevelLogicReader
{

    /// <summary>
    /// Reads the script events of a level and the order its quests lead the player through.
    /// </summary>
    /// <param name="mapPath">The path of the map file. The level script is the .lgc file of the same name next to it.</param>
    /// <param name="map">The map.</param>
    /// <param name="objects">The object table of the game.</param>
    /// <param name="quests">The quests of the game.</param>
    public static LevelLogic Read(string mapPath, MapResourceBuilder map, ObjectTable objects, QuestBook quests)
    {
        var context = new ScriptContext(map, objects, quests);
        string? scriptPath = FindLevelScript(mapPath);
        if (!objects.IsAlienShooter2)
        {
            AddLevelScriptMarkers(context.Markers, scriptPath);
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

        if (objects.IsAlienShooter2 && scriptPath is not null)
        {
            LevelScriptBlockReader.Read(File.ReadAllText(scriptPath, GameTextEncoding.Cyrillic), context);
        }

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

    // Pairs the sprites a variable wait opens with the triggers that set the variable
    private static List<OpeningTrigger> GetOpeningTriggers(ScriptContext context)
    {
        var triggers = new List<OpeningTrigger>();
        foreach (var group in context.VariableOpenings.GroupBy(opening => (opening.Name, opening.Value)))
        {
            var setters = context.GetVariableSetters(group.Key.Name, group.Key.Value).ToList();
            uint[] handles = group.Select(opening => opening.Handle).Distinct().Where(context.IsWall).ToArray();
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
            uint[] handles = group.Select(opening => opening.Handle).Distinct().ToArray();
            int timeLine = group.Key.Quest is null ? 0 : context.GetBeginTimeLine(group.Key.Quest.Value);
            triggers.Add(new OpeningTrigger(group.Key.Center, group.Key.Radius, context.GetOpeningLabel(handles[0]), handles, timeLine, true));
        }

        return triggers;
    }

    /// <summary>
    /// Returns the path of the level script of a map, or null when the map has none.
    /// </summary>
    public static string? FindLevelScript(string mapPath)
    {
        string? folder = Path.GetDirectoryName(mapPath);
        return folder is null ? null : GamePath.Resolve(folder, Path.GetFileNameWithoutExtension(mapPath) + ".lgc");
    }

    private static void AddLevelScriptMarkers(List<ScriptMarker> markers, string? scriptPath)
    {
        if (scriptPath is null)
        {
            return;
        }

        var script = LevelScriptReader.Read(scriptPath);
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
