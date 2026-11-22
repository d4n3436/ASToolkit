using ASToolkit.Core.Engine.IO;
using ASToolkit.Core.Engine.Scripts;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

public sealed partial class Map
{
    private const int FirstButtonAction = 6;
    private const int LineWait = 150;
    private const byte ElementStart = (byte)'[';
    private const string StringsFile = "Strings.ini";

    private static ReadOnlySpan<byte> MenuSection => "menu"u8;

    private static ReadOnlySpan<byte> HighSpaces => [0xcf, 0xdb, 0xe4, 0xe7, 0xe9, 0xeb, 0xed, 0xef, 0xf1, 0xf3, 0xf4, 0xf7, 0xf9, 0xfb, 0xfc, 0xfd, 0xfe];

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

    private static void ShowNextCharacter(Sprite sprite, TextSprite text)
    {
        var shown = text.Shown.Span;

        if (shown.IsEmpty || sprite.Timer != 0)
            return;

        int length = (short)shown.Length;
        if (text.VisibleCount >= length)
        {
            if (sprite.Animation != SpriteAnimation.Stand)
            {
                sprite.ChangeAnimation(SpriteAnimation.Stand);
            }

            return;
        }

        int shownCount = text.VisibleCount++;
        if (IsSpace(shown[shownCount]))
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
            while (IsSpace(shown[next]));

            if (isLineCrossed)
            {
                if (text.VisibleCount < length)
                {
                    text.VisibleCount--;
                }

                sprite.Timer = LineWait;
                SetMenuAnimation(sprite, MenuAnimation.SelectDown);
                return;
            }
        }

        SetMenuAnimation(sprite, MenuAnimation.StandUp);
        sprite.HasPlayedStateSound = false;
    }

    private static void SetMenuAnimation(Sprite sprite, MenuAnimation state) => sprite.ChangeAnimation((SpriteAnimation)state);

    [EngineRoutine(0x0046d3fe)]
    private static bool IsSpace(byte character) => CRuntime.IsWhiteSpace(character) || character > sbyte.MaxValue && HighSpaces.Contains(character);

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
            SetMenuAnimation(sprite, state == MenuAnimation.PressDown ? MenuAnimation.SelectDown : MenuAnimation.SelectUp);
            sprite.HasPlayedStateSound = true;
        }

        if (sprite.Animation == SpriteAnimation.Birth)
        {
            sprite.ChangeAnimation(SpriteAnimation.Stand);
        }

        return 0;
    }

    // TODO: Port saving the text (ACT_SAVE)
    [EngineRoutine(0x00448ff0)]
    private int TextAction(Sprite sprite, int act, int var1, int var2, int var3, Sprite? target)
    {
        var code = (ActionCode)act;
        var text = (TextSprite)sprite;
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
                ShowNextCharacter(sprite, text);
                return 0;
            default:
                return FrameAction(sprite, act, var1, var2, var3, target);
        }
    }

    private ReadOnlyMemory<byte> ReadTextAddress(int address) => address == 0 ? ReadOnlyMemory<byte>.Empty : Script.GetSlot(address - 1).Text;

    private ReadOnlyMemory<byte> ReadMenuString(ReadOnlySpan<byte> key) => ProfileFile.GetString(_fileSystem, StringsFile, MenuSection, key);

    [EngineRoutine(0x00406630)]
    private ReadOnlyMemory<byte> LoadTextFile(ReadOnlySpan<byte> name)
    {
        if (name.IsEmpty || !_fileSystem.TryRead(GameTextEncoding.Cyrillic.GetString(name), out var data))
            return ReadOnlyMemory<byte>.Empty;

        return CRuntime.GetString(data);
    }

    [EngineRoutine(0x004231c0)]
    internal ReadOnlyMemory<byte> GetVariableString(ReadOnlySpan<byte> reference)
    {
        int bracket = reference.IndexOf(ElementStart);
        int index = Script.FindObject(bracket < 0 ? reference : reference[..bracket]);
        if (index < 0)
        {
            Session.Logger.Write("!!!ERROR!!! SCRIPT Can't find variable '%s' in GetVariableString"u8, (ReadOnlyMemory<byte>)reference.ToArray());
            return ReadOnlyMemory<byte>.Empty;
        }

        int element = StackObject.ParseNumber(bracket < 0 ? ReadOnlySpan<byte>.Empty : reference[(bracket + 1)..]);
        ref var slot = ref Script.GetSlot(Script.Objects[index].Address + element);
        if (slot.Flags.HasFlag(StackObjectFlags.Number))
        {
            slot.Text = StackObject.FormatNumber(slot.Value);
        }

        return slot.Text;
    }
}
