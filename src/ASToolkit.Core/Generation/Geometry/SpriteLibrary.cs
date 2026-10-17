using System.Collections.Concurrent;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Generation.Geometry;

/// <summary>
/// Provides the sprite frames of objects from the game folder.
/// </summary>
public sealed class SpriteLibrary
{
    private readonly string _gameFolder;
    private readonly ObjectTable _objects;
    private readonly ConcurrentDictionary<string, VidFile?> _files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="SpriteLibrary"/> class.
    /// </summary>
    public SpriteLibrary(string gameFolder, ObjectTable objects)
    {
        _gameFolder = gameFolder;
        _objects = objects;
    }

    /// <summary>
    /// Returns the object and sprite file of an object ID, or null when the object or its .vid file is missing.
    /// </summary>
    public (GameObject Object, VidFile File)? Find(KnownObject id)
    {
        var obj = _objects.Find(id);
        if (obj is null)
        {
            return null;
        }

        var file = _files.GetOrAdd(obj.Path, relative =>
        {
            string? path = GamePath.Resolve(_gameFolder, relative);
            return path is not null && path.EndsWith(".vid", StringComparison.OrdinalIgnoreCase) ? VidFile.Load(path) : null;
        });
        return file is null ? null : (obj, file);
    }
}
