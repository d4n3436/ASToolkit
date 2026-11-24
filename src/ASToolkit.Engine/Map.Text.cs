using System.Text;
using ASToolkit.Engine.IO;
using ASToolkit.Engine.Scripts;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public partial class Map
{
    private const byte ElementStart = (byte)'[';
    internal const string StringsFile = "Strings.ini";

    private static ReadOnlySpan<byte> MenuSection => "menu"u8;

    [EngineRoutine(0x004495d0)]
    internal static void LayOutText(Text text)
    {
        var shown = text.Shown.Span;
        text.LineCount = 1;
        text.LineWidth = 0;
        int lineStart = 0;
        int i = 0;
        for (; i < shown.Length; i++)
        {
            if (shown[i] != CRuntime.LineFeed)
                continue;

            if (text.LineWidth < i - lineStart)
            {
                text.LineWidth = i - lineStart;
            }

            lineStart = i + 1;
            text.LineCount++;
        }

        text.VisibleCount = i;
        if (text.LineWidth == 0)
        {
            text.LineWidth = i;
        }
    }

    internal void SetText(Text text, ReadOnlyMemory<byte> description)
    {
        text.Description = description;
        text.Shown = (text.TextBehavior & TextBehavior.LoadFrom) switch
        {
            TextBehavior.NotLoad => description,
            TextBehavior.StringsIni => ReadMenuString(description.Span),
            TextBehavior.File => LoadTextFile(description.Span),
            TextBehavior.ScriptFile => LoadTextFile(GetVariableString(description.Span).Span),
            TextBehavior.ScriptStrings => ReadMenuString(GetVariableString(description.Span).Span),
            _ => GetVariableString(description.Span)
        };
        LayOutText(text);
    }

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

    private ReadOnlyMemory<byte> ReadMenuString(ReadOnlySpan<byte> key) => Profile.GetString(FileSystem, StringsFile, MenuSection, key);

}
