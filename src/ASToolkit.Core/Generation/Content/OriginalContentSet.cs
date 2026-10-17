using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Generation.Content;

/// <summary>
/// Represents the gameplay content of every original fighting level, the pool generated levels draw from.
/// </summary>
public sealed class OriginalContentSet
{
    private OriginalContentSet(IReadOnlyList<OriginalContent> levels)
    {
        Levels = levels;
    }

    /// <summary>
    /// Gets the content of each level.
    /// </summary>
    public IReadOnlyList<OriginalContent> Levels { get; }

    /// <summary>
    /// Reads the content of the given levels.
    /// </summary>
    public static OriginalContentSet Load(string gameFolder, IEnumerable<string> levels, ObjectTable objects) =>
        new(levels.Select(l => OriginalContent.Load(gameFolder, l, objects)).ToArray());

    /// <summary>
    /// Returns the content of a level by name.
    /// </summary>
    public OriginalContent GetLevel(string name) => Levels.First(l => l.Name == name);
}
