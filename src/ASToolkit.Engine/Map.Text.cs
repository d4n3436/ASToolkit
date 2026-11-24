using System.Text;
using ASToolkit.Engine.IO;
using ASToolkit.Engine.Scripts;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public sealed partial class Map
{
    private const int FirstButtonAction = 6;
    private const int LineWait = 150;
    private const byte ElementStart = (byte)'[';
    private const string StringsFile = "Strings.ini";

    private static ReadOnlySpan<byte> MenuSection => "menu"u8;

    [EngineRoutine(0x004495d0)]
    internal static void LayOutText(TextSprite text)
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

    internal void SetText(Sprite sprite, ReadOnlyMemory<byte> description)
    {
        var text = (TextSprite)sprite;
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
        int index = Script.FindObject(bracket < 0 ? reference : reference[..bracket]);
        if (index < 0)
        {
            Session.Logger.Write($"!!!ERROR!!! SCRIPT Can't find variable '{GameLogger.Decode(reference)}' in GetVariableString");
            return default;
        }

        int element = StackObject.ParseNumber(bracket < 0 ? default : reference[(bracket + 1)..]);
        ref var slot = ref Script.GetSlot(Script.Objects[index].Address + element);
        if (slot.Flags.HasFlag(StackObjectFlags.Number))
        {
            slot.Text = StackObject.FormatNumber(slot.Value);
        }

        return slot.Text;
    }

    [EngineRoutine(0x00448b30)]
    private int FrameAction(Sprite sprite, int act, int var1, int var2, int var3, Sprite? target)
    {
        var code = (ActionCode)act;
        if ((int)code < 0 || (int)code >= FirstButtonAction && code != ActionCode.NextCommand)
            return SpriteAction(sprite, act, var1, var2, var3, target);

        if ((int)code < FirstButtonAction || sprite.Animation > SpriteAnimation.Birth)
            return 0;

        var state = (MenuAnimation)sprite.Animation;
        if (state is MenuAnimation.PressDown or MenuAnimation.PressUp)
        {
            sprite.ChangeAnimation((SpriteAnimation)(state == MenuAnimation.PressDown ? MenuAnimation.SelectDown : MenuAnimation.SelectUp));
            sprite.HasPlayedStateSound = true;
        }

        if (sprite.Animation == SpriteAnimation.Birth)
        {
            sprite.ChangeAnimation(SpriteAnimation.Stand);
        }

        return 0;
    }

    // TODO: Port saving and loading the text (ACT_SAVE, ACT_LOAD) and the text address GetTextDesc and GetText return
    [EngineRoutine(0x00448ff0)]
    private int TextAction(Sprite sprite, int act, int var1, int var2, int var3, Sprite? target)
    {
        var code = (ActionCode)act;
        var text = (TextSprite)sprite;
        ReadOnlySpan<byte> shown;
        int length;
        int shownCount;
        switch (code)
        {
            case ActionCode.GetBehave:
                return (int)text.TextBehavior;
            case ActionCode.SetBehave:
                text.TextBehavior = (TextBehavior)var1;
                return 0;
            case ActionCode.SetText:
                SetText(sprite, ReadTextAddress(var1));
                return 0;
            case ActionCode.SetTextCount:
                text.VisibleCount = var1;
                return 0;
            case ActionCode.SetFile:
                text.Shown = LoadTextFile(ReadTextAddress(var1).Span);
                text.VisibleCount = text.Shown.Length;
                return 0;
            case ActionCode.GetTextDesc or ActionCode.GetText:
                return 0;
            case ActionCode.NextCommand:
                shown = text.Shown.Span;
                if (shown.IsEmpty || sprite.Timer != 0)
                    return 0;

                length = (short)shown.Length;
                if (text.VisibleCount >= length)
                {
                    if (sprite.Animation != SpriteAnimation.Stand)
                    {
                        sprite.ChangeAnimation(SpriteAnimation.Stand);
                    }

                    return 0;
                }

                shownCount = text.VisibleCount++;
                if (CRuntime.IsSpace(shown[shownCount]))
                {
                    bool isLineCrossed = false;
                    int next;
                    do
                    {
                        next = text.VisibleCount;

                        if (length <= next)
                            break;

                        if (shown[next] == CRuntime.LineFeed)
                        {
                            isLineCrossed = true;
                        }

                        text.VisibleCount = next + 1;
                    }
                    while (CRuntime.IsSpace(shown[next]));

                    if (isLineCrossed)
                    {
                        if (text.VisibleCount < length)
                        {
                            text.VisibleCount--;
                        }

                        sprite.Timer = LineWait;
                        sprite.ChangeAnimation((SpriteAnimation)MenuAnimation.SelectDown);
                        return 0;
                    }
                }

                sprite.ChangeAnimation((SpriteAnimation)MenuAnimation.StandUp);
                sprite.HasPlayedStateSound = false;
                return 0;
            default:
                return FrameAction(sprite, act, var1, var2, var3, target);
        }
    }

    private ReadOnlyMemory<byte> ReadTextAddress(int address) => address == 0 ? default : Script.GetSlot(address - 1).Text;

    private ReadOnlyMemory<byte> ReadMenuString(ReadOnlySpan<byte> key) => ProfileFile.GetString(_fileSystem, StringsFile, MenuSection, key);

    [EngineRoutine(0x00406630)]
    private ReadOnlyMemory<byte> LoadTextFile(ReadOnlySpan<byte> name)
    {
        if (name.IsEmpty || !_fileSystem.TryRead(Encoding.ASCII.GetString(name), out var data))
            return default;

        return CRuntime.GetString(data);
    }
}
