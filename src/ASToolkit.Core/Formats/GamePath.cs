namespace ASToolkit.Core.Formats;

/// <summary>
/// Provides the lookup of game files by the Windows paths the game stores.
/// </summary>
public static class GamePath
{
    /// <summary>
    /// Resolves a game path such as "vid\obj\box.vid" to the file it names inside the game folder, matching any letter case.
    /// </summary>
    /// <param name="gameFolder">The game folder.</param>
    /// <param name="relative">The game path, with backslash or slash separators.</param>
    /// <returns>The path of the file, or null if it doesn't exist.</returns>
    public static string? Resolve(string gameFolder, string relative)
    {
        string current = gameFolder;
        foreach (string part in relative.Split('\\', '/'))
        {
            string? match = Directory.EnumerateFileSystemEntries(current)
                .FirstOrDefault(e => string.Equals(Path.GetFileName(e), part, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                return null;
            }

            current = match;
        }

        return current;
    }
}
