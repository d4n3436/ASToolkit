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
    /// Initializes a new instance of the <see cref="MemoryWriteOverlay"/> class with an optional file system.
    /// </summary>
    /// <param name="fileSystem">The file system that serves the files not written, or <see langword="null"/> if there isn't one.</param>
    public MemoryWriteOverlay(IGameFileSystem? fileSystem = null)
    {
        _fileSystem = fileSystem;
    }

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
    public DateTime? GetWriteTime(string path) => _written.ContainsKey(Normalize(path)) || _deleted.Contains(Normalize(path)) ? null : _fileSystem?.GetWriteTime(path);

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
