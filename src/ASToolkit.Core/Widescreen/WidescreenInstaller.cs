using ASToolkit.Core.Patching;

namespace ASToolkit.Core.Widescreen;

/// <summary>
/// Provides the installation of the widescreen mod, which adds resolutions, a borderless window, a HUD built from code and a
/// camera that stays centered on wide screens.
/// </summary>
public static partial class WidescreenInstaller
{
    private const string ExecutableName = "AlienShooter.exe";
    private const string StatebarPath = "Maps/common_statebar.lgc";
    private const string BackupExtension = ".orig";

    /// <summary>
    /// Applies the mod to a game folder, keeping the original executable as an .orig file.
    /// </summary>
    public static void Install(string gameFolder)
    {
        string executable = Path.Combine(gameFolder, ExecutableName);
        string backup = $"{executable}{BackupExtension}";
        byte[] patched = File.ReadAllBytes(File.Exists(backup) ? backup : executable);
        ExecutablePatcher.Patch(patched);
        if (!File.Exists(backup))
        {
            File.Copy(executable, backup);
        }

        File.WriteAllBytes(executable, patched);
        RestoreBackup(Path.Combine(gameFolder, StatebarPath));
        foreach (var edit in Edits)
        {
            ScriptEditor.Apply(gameFolder, edit);
        }
    }

    /// <summary>
    /// Takes the mod out of a game folder and returns the files it changed back.
    /// </summary>
    public static string[] Remove(string gameFolder)
    {
        var restored = new List<string>();
        if (RestoreBackup(Path.Combine(gameFolder, StatebarPath)))
        {
            restored.Add(StatebarPath);
        }

        foreach (var edit in Edits.Reverse())
        {
            if (ScriptEditor.Undo(gameFolder, edit) && !restored.Contains(edit.File))
            {
                restored.Add(edit.File);
            }
        }

        foreach (string file in new[] { ExecutableName })
        {
            string path = Path.Combine(gameFolder, file);
            string backupPath = $"{path}{BackupExtension}";
            
            if (File.Exists(backupPath))
            {
                File.Copy(backupPath, path, overwrite: true);
                File.Delete(backupPath);
                restored.Add(file);
            }
        }

        return restored.ToArray();
    }

    private static bool RestoreBackup(string path)
    {
        if (!File.Exists(path + BackupExtension))
        {
            return false;
        }

        File.Move(path + BackupExtension, path, overwrite: true);
        return true;
    }
}
