using System.Text;
using System.Text.RegularExpressions;

namespace ASToolkit.Core.Patching;

/// <summary>
/// Provides the applying and undoing of text edits in the game's scripts and string tables.
/// </summary>
internal static class ScriptEditor
{
    /// <summary>
    /// Applies an edit and returns whether the file changed.
    /// </summary>
    public static bool Apply(string gameFolder, ScriptEdit edit) => Replace(gameFolder, edit, undo: false);

    /// <summary>
    /// Puts an edit's old text back and returns whether the file changed.
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

        int found = Regex.Count(text, Regex.Escape(from));
        if (found == 0 && undo)
        {
            return false;
        }

        if (found != edit.Count)
        {
            throw new InvalidDataException($"{edit.File}: expected {edit.Count} of '{from.Split('\n')[0].Trim()}' but found {found}.");
        }

        File.WriteAllBytes(path, Encoding.Latin1.GetBytes(text.Replace(from, to, StringComparison.Ordinal)));
        return true;
    }
}
