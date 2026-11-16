using System.Text;

namespace ASToolkit.Core.Engine.Scripts;

/// <summary>
/// Provides one script file from memory in front of other script files.
/// </summary>
public sealed class ScriptOverlay : IScriptFileSource
{
    private readonly IScriptFileSource _files;
    private readonly string _path;
    private readonly byte[] _text;

    /// <summary>
    /// Creates an overlay serving a script text at a path.
    /// </summary>
    /// <param name="files">The script files read for every other path.</param>
    /// <param name="path">The path the text is served at, compared in any letter case and with either slash.</param>
    /// <param name="text">The script text.</param>
    public ScriptOverlay(IScriptFileSource files, string path, string text)
    {
        _files = files;
        _path = Normalize(path);
        _text = Encoding.Latin1.GetBytes(text);
    }

    /// <inheritdoc/>
    public bool TryRead(string path, out ReadOnlyMemory<byte> data)
    {
        if (!Normalize(path).Equals(_path, StringComparison.OrdinalIgnoreCase))
            return _files.TryRead(path, out data);

        data = _text;

        return true;
    }

    private static string Normalize(string path) => path.Replace('/', '\\');
}
