namespace ASToolkit.Core.Engine.IO;

/// <summary>
/// Defines the file operations the engine uses in place of the .NET file APIs, such as reading, writing, deleting and renaming files in the game folder.
/// </summary>
public interface IGameFileSystem
{
    /// <summary>
    /// Reads a file, returning false when it cannot be opened.
    /// </summary>
    /// <param name="path">The path of the file, relative to the game folder.</param>
    /// <param name="data">The bytes of the file.</param>
    /// <returns><see langword="true"/> when the file was read; otherwise, <see langword="false"/>.</returns>
    bool TryRead(string path, out ReadOnlyMemory<byte> data);

    /// <summary>
    /// Opens a file for writing, creating it when it does not exist.
    /// </summary>
    /// <param name="path">The path of the file, relative to the game folder.</param>
    /// <param name="append"><see langword="true"/> to write after the existing bytes; <see langword="false"/> to replace them.</param>
    /// <returns>The stream to write to, or <see langword="null"/> when the file cannot be opened.</returns>
    Stream? OpenWrite(string path, bool append) => null;

    /// <summary>
    /// Deletes a file.
    /// </summary>
    /// <param name="path">The path of the file, relative to the game folder.</param>
    /// <returns><see langword="true"/> when the file was deleted; otherwise, <see langword="false"/>.</returns>
    bool Delete(string path) => false;

    /// <summary>
    /// Renames a file.
    /// </summary>
    /// <param name="source">The path of the file, relative to the game folder.</param>
    /// <param name="destination">The new path, which must not exist.</param>
    /// <returns><see langword="true"/> when the file was renamed; otherwise, <see langword="false"/>.</returns>
    bool Move(string source, string destination) => false;
}
