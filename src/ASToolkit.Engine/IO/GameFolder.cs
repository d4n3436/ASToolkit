using ASToolkit.Formats;

namespace ASToolkit.Engine.IO;

/// <summary>
/// Represents a game folder on disk, its paths matched without regard to case.
/// </summary>
public sealed class GameFolder : IGameFileSystem
{
    private readonly string _gameFolder;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameFolder"/> class over a game folder.
    /// </summary>
    /// <param name="gameFolder">The folder that holds the game's executable.</param>
    public GameFolder(string gameFolder)
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

    /// <inheritdoc/>
    public Stream? OpenWrite(string path, bool append)
    {
        string? file = ResolveForWrite(path);

        if (file is null)
            return null;

        try
        {
             return new FileStream(file, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public DateTime? GetWriteTime(string path)
    {
        string? file = GamePath.Resolve(_gameFolder, path);

        return file is not null && File.Exists(file) ? File.GetLastWriteTime(file) : null;
    }

    /// <inheritdoc/>
    public bool Delete(string path)
    {
        string? file = GamePath.Resolve(_gameFolder, path);

        if (file is null || !File.Exists(file))
            return false;

        File.Delete(file);

        return true;
    }

    /// <inheritdoc/>
    public bool Move(string source, string destination)
    {
        string? from = GamePath.Resolve(_gameFolder, source);
        string? to = ResolveForWrite(destination);

        if (from is null || to is null || !File.Exists(from) || File.Exists(to))
            return false;

        File.Move(from, to);

        return true;
    }

    private string? ResolveForWrite(string path)
    {
        int separator = path.LastIndexOfAny(['\\', '/']);
        string? folder = separator < 0 ? _gameFolder : GamePath.Resolve(_gameFolder, path[..separator]);

        if (folder is null || !Directory.Exists(folder))
            return null;

        string name = path[(separator + 1)..];
        string? existing = GamePath.Resolve(folder, name);

        return existing ?? Path.Combine(folder, name);
    }
}
