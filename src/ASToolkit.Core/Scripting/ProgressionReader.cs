using System.Numerics;

namespace ASToolkit.Core.Scripting;

// Reads the places the quests of a level lead the player to, in story order
internal static class ProgressionReader
{
    private const string BegunState = "QS_BEGUN";
    private const string DeathEvent = "CSI_DEATH";
    private const float ShotReach = 450;
    private const int MainQuestLine = 1;

    private static readonly Dictionary<string, string> EventActions = new()
    {
        ["CSI_DEATH"] = "destroy {0}",
        ["CSI_TAKE"] = "pick up {0}",
        ["CSI_HAVE"] = "carry {0}",
        ["CSI_BIRTH"] = "wait for {0} to appear"
    };

    public static ProgressionStep[] Read(ScriptContext context)
    {
        if (!context.Objects.IsAlienShooter2)
        {
            return [];
        }

        var involved = context.QuestStarts.Select(start => start.Quest)
            .Concat(context.WaitedQuests.Select(key => context.Quests.GetQuest(key.Line, key.Sub)).OfType<Quest>())
            .Where(quest => quest.Line == MainQuestLine)
            .DistinctBy(quest => quest.Key)
            .OrderBy(quest => quest.TimeLine)
            .ThenBy(quest => quest.Line)
            .ThenBy(quest => quest.Sub)
            .ToArray();
        
        var steps = new List<ProgressionStep>();
        if (!AddQuestSteps(context, involved, steps) && !AddDownloadSteps(context, involved, steps))
        {
            AddExitSteps(context, involved, steps);
        }

        return steps.ToArray();
    }

    // Finds the quests the story does not lead through that open sprites when they begin
    public static List<QuestDetour> ReadDetours(ScriptContext context, ProgressionStep[] progression)
    {
        var detours = new List<QuestDetour>();
        foreach (var key in context.Openings.Values.SelectMany(quests => quests).OfType<(int Line, int Sub)>().Distinct())
        {
            var quest = context.Quests.GetQuest(key.Line, key.Sub);
            if (quest is null || progression.Any(step => step.Label.Contains($"quest {quest.Key}")))
            {
                continue;
            }

            var steps = new List<ProgressionStep>();
            AddItemSteps(context, quest, steps, false);
            if (steps.Count > 0)
            {
                detours.Add(new QuestDetour(context.GetBeginTimeLine(key), steps));
            }
        }

        return detours.OrderBy(detour => detour.TimeLine).ToList();
    }

    // Adds the steps of each quest and tells whether the story ends with one of them
    private static bool AddQuestSteps(ScriptContext context, Quest[] involved, List<ProgressionStep> steps)
    {
        foreach (var quest in involved)
        {
            var starts = context.QuestStarts
                .Where(start => start.Quest.Key == quest.Key && start.Radius > 0)
                .ToArray();
            
            var unused = starts
                .Where(start => steps.All(step => step.Position != start.Position))
                .ToArray();
            
            var places = unused.Length > 0 ? unused : starts;
            bool hasStart = places.Length > 0;
            if (hasStart)
            {
                var place = steps.Count == 0 ? places[0] : places.MinBy(start => Vector3.Distance(start.Position, steps[^1].Position));
                if (quest.WinsOnBegin)
                {
                    steps.Add(new ProgressionStep(place.Position, place.Radius, $"start quest {quest.Key} and win the level: {quest.Title}", quest.TimeLine - 1));
                    return true;
                }

                steps.Add(new ProgressionStep(place.Position, place.Radius, $"start quest {quest.Key}: {quest.Title}", quest.TimeLine - 1));
            }

            hasStart = AddItemSteps(context, quest, steps, hasStart);
            if (hasStart && context.FollowingQuests.Any(follow => follow.After == (quest.Line, quest.Sub)
                    && context.Quests.GetQuest(follow.Quest.Line, follow.Quest.Sub)?.WinsOnBegin == true))
            {
                return true;
            }
        }

        return false;
    }

    // Adds a step for each sprite of the object events a quest reacts to, nearest first, and tells whether the quest has a start step
    private static bool AddItemSteps(ScriptContext context, Quest quest, List<ProgressionStep> steps, bool hasStart)
    {
        foreach (var item in quest.Items)
        {
            var remaining = context.GetNamedSprites(item.Name)
                .Select(context.GetPlace)
                .Distinct()
                .ToList();
            
            if (remaining.Count == 0)
            {
                continue;
            }

            bool isTarget = item.Event == DeathEvent;
            bool startsQuest = !hasStart && item.State == BegunState;
            string action = EventActions.TryGetValue(item.Event, out string? verb) ? string.Format(verb, item.Name) : $"wait for {item.Event} of {item.Name}";
            string label = startsQuest ? $"{action} to start quest {quest.Key}: {quest.Title}" : $"{action} for quest {quest.Key}: {quest.Title}";
            int timeLine = startsQuest ? quest.TimeLine - 1 : quest.TimeLine;
            int count = isTarget ? remaining.Count : 1;
            for (int added = 0; added < count; added++)
            {
                var from = steps.Count > 0 ? steps[^1].Position : remaining[0];
                var place = remaining.MinBy(place => Vector3.Distance(place, from));
                remaining.Remove(place);
                steps.Add(new ProgressionStep(place, isTarget ? ShotReach : 0, label, timeLine, isTarget));
            }

            hasStart |= startsQuest;
        }

        return hasStart;
    }

    // Adds a step for each download and tells whether the story ends with them
    private static bool AddDownloadSteps(ScriptContext context, Quest[] involved, List<ProgressionStep> steps)
    {
        var winning = involved.FirstOrDefault(quest => quest.WinsOnBegin);
        int timeLine = (winning is null ? null : context.Quests.FindPrevious(winning))?.TimeLine ?? int.MaxValue - 1;
        foreach (var download in context.Downloads.OrderBy(download => download.Order))
        {
            steps.Add(new ProgressionStep(download.Position, download.Radius, "download the data of the computer", timeLine));
        }

        return context.Downloads.Count > 0 && winning is not null;
    }

    // Adds the exit areas of the last quest that has one
    private static void AddExitSteps(ScriptContext context, Quest[] involved, List<ProgressionStep> steps)
    {
        var keys = involved
            .Select(quest => (quest.Line, quest.Sub))
            .ToHashSet();
        
        var started = context.QuestStarts
            .Select(start => (start.Quest.Line, start.Quest.Sub))
            .ToHashSet();
        
        var candidates = context.Exits
            .Where(exit => exit.Quest is null || keys.Contains(exit.Quest.Value))
            .ToArray();
        
        var preferred = candidates
            .Where(exit => exit.Quest is null || started.Contains(exit.Quest.Value))
            .ToArray();
        
        var exits = (preferred.Length > 0 ? preferred : candidates)
            .GroupBy(exit => exit.Quest is null ? -1 : context.Quests.GetQuest(exit.Quest.Value.Line, exit.Quest.Value.Sub)?.TimeLine ?? -1)
            .MaxBy(group => group.Key);
        
        foreach (var exit in exits?.DistinctBy(exit => exit.Position) ?? [])
        {
            steps.Add(new ProgressionStep(exit.Position, exit.Radius, $"end the level: {exit.Label}", int.MaxValue));
        }
    }
}
