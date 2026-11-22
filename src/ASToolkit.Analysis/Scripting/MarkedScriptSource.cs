using System.Text;
using ASToolkit.Engine.IO;
using ASToolkit.Engine.Scripts;

namespace ASToolkit.Analysis.Scripting;

internal sealed class MarkedScriptSource : IGameFileSystem
{
    private const string IncludeMarker = "_IncludeEnd";

    private static ReadOnlySpan<byte> RootMarker => "_RootEnd"u8;
    private readonly IGameFileSystem? _fileSystem;
    private readonly ReadOnlyMemory<byte> _rootText;
    private bool _rootRead;
    private int _includes;

    public MarkedScriptSource(IGameFileSystem? fileSystem, ReadOnlyMemory<byte> rootText = default)
    {
        _fileSystem = fileSystem;
        _rootText = rootText;
    }

    public bool TryRead(string path, out ReadOnlyMemory<byte> data)
    {
        bool isRoot = !_rootRead;
        _rootRead = true;
        if (isRoot && !_rootText.IsEmpty)
        {
            data = _rootText;
        }
        else if (_fileSystem is null || !_fileSystem.TryRead(path, out data))
        {
            data = default;
            return false;
        }

        var marker = isRoot ? RootMarker : Encoding.ASCII.GetBytes($"{IncludeMarker}{++_includes}");
        byte[] marked = new byte[data.Length + marker.Length + 8];
        data.Span.CopyTo(marked);
        int at = data.Length;
        marked[at++] = (byte)'\n';
        marker.CopyTo(marked.AsSpan(at));
        at += marker.Length;
        "(){}\n"u8.CopyTo(marked.AsSpan(at));
        data = marked.AsMemory(0, at + 5);

        return true;
    }

    public static Range GetRootCode(Script script)
    {
        int start = 0;
        int end = script.Code.Length;
        foreach (var entry in script.Objects)
        {
            if (entry.Kind != ScriptObjectKind.Function)
                continue;

            if (entry.Name.Span.SequenceEqual(RootMarker))
            {
                end = entry.Address;
            }
            else if (entry.Name.Span.StartsWith("_IncludeEnd"u8))
            {
                start = Math.Max(start, entry.Address + 1);
            }
        }

        return start..end;
    }
}
