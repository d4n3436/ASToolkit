using System.Text;
using ASToolkit.Core.Engine.IO;

namespace ASToolkit.Core.Engine.Scripts;

/// <summary>
/// Represents a file system that serves one script from memory and passes every other path to an inner file system.
/// </summary>
public sealed class ScriptOverlay : IGameFileSystem
{
    private readonly IGameFileSystem _fileSystem;
    private readonly string _path;
    private readonly byte[] _text;

    /// <summary>
    /// Creates an overlay serving a script text at a path.
    /// </summary>
    /// <param name="fileSystem">The file system that serves every other path.</param>
    /// <param name="path">The path the text is served at, compared in any letter case and with either slash.</param>
    /// <param name="text">The script text.</param>
    public ScriptOverlay(IGameFileSystem fileSystem, string path, string text)
    {
        _fileSystem = fileSystem;
        _path = Normalize(path);
        _text = Encoding.Latin1.GetBytes(text);
    }

    /// <inheritdoc/>
    public bool TryRead(string path, out ReadOnlyMemory<byte> data)
    {
        if (!Normalize(path).Equals(_path, StringComparison.OrdinalIgnoreCase))
            return _fileSystem.TryRead(path, out data);

        data = _text;

        return true;
    }

    /// <inheritdoc/>
    public Stream? OpenWrite(string path, bool append) => _fileSystem.OpenWrite(path, append);

    /// <inheritdoc/>
    public bool Delete(string path) => _fileSystem.Delete(path);

    /// <inheritdoc/>
    public bool Move(string source, string destination) => _fileSystem.Move(source, destination);

    private static string Normalize(string path) => path.Replace('/', '\\');
}
