namespace ASToolkit.Core.Formats;

/// <summary>
/// Provides the lookup of game files by the Windows paths the game stores.
/// </summary>
public static class GamePath
{
    /// <summary>
    /// Returns the file a game path such as "vid\obj\box.vid" names inside the game folder, matching any letter case,
    /// or null when it doesn't exist.
    /// </summary>
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
