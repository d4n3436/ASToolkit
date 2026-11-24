using System.Text;
using ASToolkit.Formats;

namespace ASToolkit.Generation.Patching;

internal static class ScriptEditor
{
    public static bool Apply(string gameFolder, ScriptEdit edit) => Replace(gameFolder, edit, undo: false);

    public static bool Undo(string gameFolder, ScriptEdit edit) => Replace(gameFolder, edit, undo: true);

    private static bool Replace(string gameFolder, ScriptEdit edit, bool undo)
    {
        string path = Path.Combine(gameFolder, edit.File);
        if (!File.Exists(path))
        {
            return false;
        }

        string text = GameTextEncoding.Cyrillic.GetString(File.ReadAllBytes(path));
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

        File.WriteAllBytes(path, GameTextEncoding.Cyrillic.GetBytes(text.Replace(from, to, StringComparison.Ordinal)));
        return true;
    }
}
