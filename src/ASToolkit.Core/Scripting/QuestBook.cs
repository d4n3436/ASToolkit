using System.Text.RegularExpressions;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Scripting;

/// <summary>
/// Provides the quests of levels.cfg with their titles and the dialog texts of a game folder.
/// </summary>
public sealed partial class QuestBook
{
    private const string QuestsFile = "levels.cfg";
    private const string MissionsFile = "Text/mission.txt";
    private const string DialogsFile = "Text/dialogs.txt";
    private const int QuestLineSize = 1000;

    private readonly Dictionary<(int Line, int Sub), Quest> _quests;
    private readonly Dictionary<int, string> _dialogs;

    private QuestBook(Dictionary<(int Line, int Sub), Quest> quests, Dictionary<int, string> dialogs)
    {
        _quests = quests;
        _dialogs = dialogs;
    }

    /// <summary>
    /// Gets an empty book for games without quests.
    /// </summary>
    public static QuestBook Empty { get; } = new([], []);

    /// <summary>
    /// Loads the quests and texts of a game folder, or returns an empty book when the folder has no levels.cfg.
    /// </summary>
    public static QuestBook Load(string gameFolder)
    {
        string? questsPath = GamePath.Resolve(gameFolder, QuestsFile);
        if (questsPath is null)
        {
            return Empty;
        }

        var titles = ReadSections(GamePath.Resolve(gameFolder, MissionsFile));
        var dialogs = new Dictionary<int, string>();
        foreach ((string key, var lines) in ReadSections(GamePath.Resolve(gameFolder, DialogsFile)))
        {
            if (int.TryParse(key, out int number) && lines.Count > 0)
            {
                dialogs[number] = lines[0];
            }
        }

        var quests = new Dictionary<(int Line, int Sub), Quest>();
        foreach (var block in ReadBlocks(File.ReadAllText(questsPath, GameTextEncoding.Cyrillic)))
        {
            var match = QuestName.Match(block.GetValueOrDefault("Quest", [""])[0]);
            if (!match.Success)
            {
                continue;
            }

            int line = int.Parse(match.Groups[2].ValueSpan);
            int sub = int.Parse(match.Groups[3].ValueSpan);
            string key = match.Value;
            string title = titles.TryGetValue(key, out var titleLines) ? string.Join(" ", titleLines).Trim() : "";
            if (title.Length == 0)
            {
                title = block.GetValueOrDefault("Comment", [""])[0];
            }

            var items = block.GetValueOrDefault("ChangeStateItem", [])
                .Select(value => value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Where(parts => parts.Length == 3)
                .Select(parts => new QuestItem(parts[0], parts[1], parts[2]))
                .ToArray();
            
            int timeLine = int.TryParse(block.GetValueOrDefault("TimeLine", ["0"])[0], out int parsed) ? parsed : 0;
            bool winsOnBegin = int.TryParse(block.GetValueOrDefault("WinOnBegin", ["0"])[0], out int win) && win != 0;
            
            var next = block.GetValueOrDefault("NextQuests", [])
                .SelectMany(value => value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Select(value => int.TryParse(value, out int number) ? number : 0)
                .Where(number => number > 0)
                .Select(number => (number / QuestLineSize, number % QuestLineSize))
                .ToArray();
            
            quests.TryAdd((line, sub), new Quest(line, sub, timeLine, title, items, winsOnBegin) { NextQuests = next });
        }

        return new QuestBook(quests, dialogs);
    }

    /// <summary>
    /// Returns the quest that is followed by a quest, or null when none is.
    /// </summary>
    /// <param name="quest">The quest.</param>
    public Quest? FindPrevious(Quest quest) => _quests.Values.FirstOrDefault(candidate => candidate.NextQuests.Contains((quest.Line, quest.Sub)));

    /// <summary>
    /// Returns the quest of a line and number, or null when the book has no such quest.
    /// </summary>
    public Quest? GetQuest(int line, int sub) => _quests.GetValueOrDefault((line, sub));

    /// <summary>
    /// Returns the first text line of a dialog, or an empty string when the book has no such dialog.
    /// </summary>
    public string GetDialog(int number) => _dialogs.GetValueOrDefault(number, "");

    private static List<Dictionary<string, List<string>>> ReadBlocks(string text)
    {
        var blocks = new List<Dictionary<string, List<string>>> { new() };
        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.StartsWith(';'))
            {
                if (blocks[^1].Count > 0)
                {
                    blocks.Add([]);
                }

                continue;
            }

            int equals = line.IndexOf('=');
            if (equals > 0)
            {
                string key = line[..equals].Trim();
                blocks[^1].TryAdd(key, []);
                blocks[^1][key].Add(line[(equals + 1)..].Trim());
            }
        }

        return blocks;
    }

    private static Dictionary<string, List<string>> ReadSections(string? path)
    {
        var sections = new Dictionary<string, List<string>>();
        if (path is null)
        {
            return sections;
        }

        string? current = null;
        foreach (string rawLine in File.ReadAllLines(path, GameTextEncoding.Cyrillic))
        {
            string line = rawLine.Trim();
            var header = SectionHeader.Match(line);
            if (header.Success)
            {
                current = header.Groups[1].Value;
                sections.TryAdd(current, []);
                continue;
            }

            string text = Tag.Replace(line, "").Trim();
            if (current is not null && text.Length > 0 && !line.StartsWith(';'))
            {
                sections[current].Add(text);
            }
        }

        return sections;
    }

    [GeneratedRegex(@"^(\d+)_(\d+)_(\d+)$")]
    private static partial Regex QuestName { get; }

    [GeneratedRegex(@"^<([0-9_]+)>$")]
    private static partial Regex SectionHeader { get; }

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex Tag { get; }
}
