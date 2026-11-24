using System.Text;
using ASToolkit.Engine.IO;
using ASToolkit.Engine.Scripts;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public partial class Map
{
    private const byte ElementStart = (byte)'[';
    internal const string StringsFile = "Strings.ini";

    [EngineRoutine(0x004231c0)]
    internal ReadOnlyMemory<byte> GetVariableString(ReadOnlySpan<byte> reference)
    {
        int bracket = reference.IndexOf(ElementStart);
        int index = Script.Objects.Location(bracket < 0 ? reference : reference[..bracket]);
        if (index < 0)
        {
            Session.Error.Write($"!!!ERROR!!! SCRIPT Can't find variable '{MyError.Decode(reference)}' in GetVariableString");
            return default;
        }

        int element = ScriptStack.ParseNumber(bracket < 0 ? default : reference[(bracket + 1)..]);
        ref var slot = ref Script.GetSlot(Script.Objects[index].Address + element);
        if (slot.Flags.HasFlag(ScriptStackFlags.Number))
        {
            slot.Text = ScriptStack.FormatNumber(slot.Value);
        }

        return slot.Text;
    }

    [EngineRoutine(0x00406630)]
    internal ReadOnlyMemory<byte> LoadTextFile(ReadOnlySpan<byte> name)
    {
        if (name.IsEmpty || !FileSystem.TryRead(Encoding.ASCII.GetString(name), out var data))
            return default;

        return CRuntime.GetString(data);
    }
}
