using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine.Scripts;

/// <summary>
/// Provides the script files of a game folder by the path's scripts name, in any letter case.
/// </summary>
public sealed class ScriptFolder : IScriptFileSource
{
    private readonly string _gameFolder;

    /// <summary>
    /// Creates the script files of a game folder.
    /// </summary>
    /// <param name="gameFolder">The game folder the engine runs in.</param>
    public ScriptFolder(string gameFolder)
    {
        _gameFolder = gameFolder;
    }

    /// <inheritdoc/>
    public bool TryRead(string path, out ReadOnlyMemory<byte> data)
    {
        string? file = GamePath.Resolve(_gameFolder, path);
        bool found = file is not null && File.Exists(file);
        data = found ? File.ReadAllBytes(file!) : null;

        return found;
    }
}
