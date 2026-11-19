namespace ASToolkit.Core.Engine.IO;

/// <summary>
/// Represents a file system that keeps its writes in memory and reads the other files from an inner file system.
/// </summary>
public sealed class MemoryWriteOverlay : IGameFileSystem
{
    private readonly IGameFileSystem? _fileSystem;
    private readonly Dictionary<string, MemoryStream> _written = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _deleted = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates an overlay over a file system, or over nothing.
    /// </summary>
    /// <param name="fileSystem">The file system that serves the files not written, or <see langword="null"/> for none.</param>
    public MemoryWriteOverlay(IGameFileSystem? fileSystem = null)
    {
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Gets the paths of the files written and not deleted.
    /// </summary>
    public IEnumerable<string> WrittenPaths => _written.Keys;

    /// <inheritdoc/>
    public bool TryRead(string path, out ReadOnlyMemory<byte> data)
    {
        string key = Normalize(path);

        if (_written.TryGetValue(key, out var stream))
        {
            data = stream.ToArray();

            return true;
        }

        data = default;

        return !_deleted.Contains(key) && _fileSystem is not null && _fileSystem.TryRead(path, out data);
    }

    /// <inheritdoc/>
    public Stream? OpenWrite(string path, bool append)
    {
        string key = Normalize(path);
        var stream = new MemoryStream();

        if (append && TryRead(path, out var existing))
        {
            stream.Write(existing.Span);
        }

        _written[key] = stream;
        _deleted.Remove(key);

        return stream;
    }

    /// <inheritdoc/>
    public bool Delete(string path)
    {
        if (!TryRead(path, out _))
            return false;

        string key = Normalize(path);
        _written.Remove(key);
        _deleted.Add(key);

        return true;
    }

    /// <inheritdoc/>
    public bool Move(string source, string destination)
    {
        if (TryRead(destination, out _) || !TryRead(source, out var data))
            return false;

        var stream = new MemoryStream();
        stream.Write(data.Span);
        Delete(source);
        string key = Normalize(destination);
        _written[key] = stream;
        _deleted.Remove(key);

        return true;
    }

    private static string Normalize(string path) => path.Replace('/', '\\');
}
