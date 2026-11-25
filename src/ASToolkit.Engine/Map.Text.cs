using System.Text;
using ASToolkit.Engine.IO;

namespace ASToolkit.Engine;

public partial class Map
{
    internal const string StringsFile = "Strings.ini";

    [EngineRoutine(0x00406630)]
    internal ReadOnlyMemory<byte> LoadTextFile(ReadOnlySpan<byte> name)
    {
        if (name.IsEmpty || !FileSystem.TryRead(Encoding.ASCII.GetString(name), out var data))
            return default;

        return CRuntime.GetString(data);
    }
}
