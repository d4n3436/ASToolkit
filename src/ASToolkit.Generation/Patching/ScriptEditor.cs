using System.Text;

namespace ASToolkit.Generation.Patching;

/// <summary>
/// Provides methods for applying and undoing text edits in the game's scripts and string tables.
/// </summary>
internal static class ScriptEditor
{
    /// <summary>
    /// Applies an edit to a file in a game folder.
    /// </summary>
    public static bool Apply(string gameFolder, ScriptEdit edit) => Replace(gameFolder, edit, undo: false);

    /// <summary>
    /// Puts the old text of an edit back in a file in a game folder.
    /// </summary>
    public static bool Undo(string gameFolder, ScriptEdit edit) => Replace(gameFolder, edit, undo: true);

    private static bool Replace(string gameFolder, ScriptEdit edit, bool undo)
    {
        string path = Path.Combine(gameFolder, edit.File);
        if (!File.Exists(path))
        {
            return false;
        }

        string text = Encoding.Latin1.GetString(File.ReadAllBytes(path));
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string old = edit.Old.Replace("\n", newline, StringComparison.Ordinal);
        string replacement = edit.New.Replace("\n", newline, StringComparison.Ordinal);
        (string from, string to) = undo ? (replacement, old) : (old, replacement);
        if (!undo && text.Contains(replacement, StringComparison.Ordinal))
        {
            return false;
        }

        int found = text.AsSpan().Count(from);
        if (found == 0 && undo)
        {
            return false;
        }

        if (found != edit.Count)
        {
            throw new InvalidDataException($"The edit in {edit.File} expects {edit.Count} occurrences of '{from.Split('\n')[0].Trim()}', but the file has {found}.");
        }

        File.WriteAllBytes(path, Encoding.Latin1.GetBytes(text.Replace(from, to, StringComparison.Ordinal)));
        return true;
    }
}
