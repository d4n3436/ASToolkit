namespace ASToolkit.Core.Engine.Scripts;

/// <summary>
/// Defines a source that opens script files by path.
/// </summary>
public interface IScriptFileSource
{
    /// <summary>
    /// Reads a script file, returning false when it cannot be opened.
    /// </summary>
    /// <param name="path">The path the script names.</param>
    /// <param name="data">The bytes of the file.</param>
    /// <returns><see langword="true"/> when the file was read; otherwise, <see langword="false"/>.</returns>
    bool TryRead(string path, out ReadOnlyMemory<byte> data);
}
