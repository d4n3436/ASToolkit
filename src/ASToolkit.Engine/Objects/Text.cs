using System.Numerics;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a sprite that shows a text letter by letter.
/// </summary>
public sealed class Text : Frame
{
    private const int MinDirections = 0x7f;
    private const byte FontSwitch = 0x1b;
    private const byte Tab = (byte)'\t';
    private const byte TagStart = (byte)'<';
    private const byte TagEnd = (byte)'>';
    private const byte FirstPrintable = 0x20;
    private const int TabStops = 7;
    private const float Half = 0.5f;
    private const int LineWait = 150;

    /// <summary>
    /// Initializes a new instance of the <see cref="Text"/> class.
    /// </summary>
    /// <param name="number">The number of the sprite.</param>
    /// <param name="vid">The shared data and frames of the sprite's object ID.</param>
    /// <param name="position">The position of the sprite.</param>
    /// <param name="army">The army of the sprite.</param>
    /// <param name="hitPoints">The hit points of the sprite.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    /// <param name="direction">The direction of the sprite.</param>
    public Text(int number, Vid vid, Vector3 position, Army army, int hitPoints, Map map, Angle direction = default)
        : base(number, vid, position, army, hitPoints, map, direction)
    {
    }

    /// <summary>
    /// Gets or sets the text the sprite shows.
    /// </summary>
    public ReadOnlyMemory<byte> Shown { get; set; }

    /// <summary>
    /// Gets or sets the text the sprite was given, or the name of the script variable it shows.
    /// </summary>
    public ReadOnlyMemory<byte> Description { get; set; }

    /// <summary>
    /// Gets or sets the number of letters shown so far.
    /// </summary>
    public int VisibleCount { get; set; }

    /// <summary>
    /// Gets or sets how the text is filled in and shown.
    /// </summary>
    public TextBehavior TextBehavior { get; set; }

    /// <summary>
    /// Gets or sets the number of lines of the text.
    /// </summary>
    public int LineCount { get; set; } = 1;

    /// <summary>
    /// Gets or sets the width of the longest line, in pixels.
    /// </summary>
    public int LineWidth { get; set; }

    private static ReadOnlySpan<byte> FontTag => "<Font="u8;

    /// <summary>
    /// Draws the shown text with the frames of the font's object ID.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="map">The map.</param>
    /// <param name="camera">The top left corner of the view, in map pixels.</param>
    [EngineRoutine(0x00448ba0)]
    public override void Draw(Screen screen, Map map, Vector2 camera)
    {
        if (Vid == map.Vids.Empty || Vid.Directions < MinDirections)
        {
            map.Session.Graphics.DrawDeviceText(new Vector2(Position.X, Position.Y), uint.MaxValue, Shown.Span);
            return;
        }

        var savedPosition = Position;
        int savedFrame = Frame;

        if ((TextBehavior & TextBehavior.LoadFrom) == TextBehavior.ScriptUpdated)
        {
            Shown = map.GetVariableString(Description.Span);
            Map.LayOutText(this);
        }

        var shown = Shown.Span;

        if (!shown.IsEmpty && shown[0] != 0)
        {
            var size = Vid.Object.Size.AsVector2();
            var half = Vid.Half;
            var pen = new Vector2(
                TextBehavior.HasFlag(TextBehavior.CenterX)
                    ? savedPosition.X - (LineWidth - 1) * size.X * Half
                    : TextBehavior.HasFlag(TextBehavior.Right) ? savedPosition.X - ((LineWidth - 1) * size.X + half.X) : half.X + savedPosition.X,
                TextBehavior.HasFlag(TextBehavior.CenterY)
                    ? savedPosition.Y - (LineCount - 1) * size.Y * Half
                    : TextBehavior.HasFlag(TextBehavior.Bottom) ? savedPosition.Y - ((LineCount - 1) * size.Y + half.Y) : half.Y + savedPosition.Y);
            float lineStart = pen.X - size.X;
            var current = Vid;
            int i = 0;

            while (GetCharacter(shown, i) != 0 && i < VisibleCount)
            {
                byte character = shown[i];
                var currentSize = current.Object.Size.AsVector2();

                if (character == CRuntime.LineFeed)
                {
                    pen.X = lineStart;
                    pen.Y += currentSize.Y;
                }
                else if (character == CRuntime.CarriageReturn)
                {
                    pen.X = lineStart;
                }
                else if (character == Tab)
                {
                    pen.X += currentSize.X * TabStops;
                }
                else if (character == TagStart && shown.StartsWith(FontTag))
                {
                    var identifier = shown[FontTag.Length..];
                    int id = identifier.Length > 1 && identifier[1] == (byte)'x' ? CRuntime.ScanInteger(identifier) : CRuntime.ParseInteger(identifier);
                    current = map.Vids.Find((KnownObject)id) ?? map.Vids.Empty;
                    pen.X -= current.Object.Size.X;
                    int end = shown.IndexOf(TagEnd);
                    i += end < 0 ? shown.Length : end;
                }
                else if (character == FontSwitch && i + 1 < VisibleCount && map.Vids.Find((KnownObject)shown[i + 1]) is not null)
                {
                    int id = (sbyte)shown[i + 1];
                    i++;
                    current = map.Vids.Find((KnownObject)id) ?? map.Vids.Empty;
                    pen.X -= current.Object.Size.X;
                }
                else if (character >= FirstPrintable)
                {
                    Position = savedPosition with { X = pen.X, Y = pen.Y };
                    Frame = character;
                    current.Draw(screen, map, this, camera);
                }

                i++;
                pen.X += current.Object.Size.X;
            }
        }

        Position = savedPosition;
        Frame = savedFrame;
    }

    // TODO: Port saving the text (ACT_SAVE) and the text address GetTextDesc and GetText return
    [EngineRoutine(0x00448ff0)]
    internal override int Action(int act, int var1, int var2, int var3, Sprite? target)
    {
        var code = (ActionCode)act;
        switch (code)
        {
            case ActionCode.GetBehave:
                return (int)TextBehavior;
            case ActionCode.SetBehave:
                TextBehavior = (TextBehavior)var1;
                return 0;
            case ActionCode.SetText:
                Map.SetText(this, ReadTextAddress(var1));
                return 0;
            case ActionCode.SetTextCount:
                VisibleCount = var1;
                return 0;
            case ActionCode.SetFile:
                Shown = Map.LoadTextFile(ReadTextAddress(var1).Span);
                VisibleCount = Shown.Length;
                return 0;
            case ActionCode.GetTextDesc or ActionCode.GetText:
                return 0;
            case ActionCode.NextCommand:
                var shown = Shown.Span;
                if (shown.IsEmpty || Timer != 0)
                    return 0;

                int length = (short)shown.Length;
                if (VisibleCount >= length)
                {
                    if (Animation != SpriteAnimation.Stand)
                    {
                        ChangeAnimation(SpriteAnimation.Stand);
                    }

                    return 0;
                }

                int shownCount = VisibleCount++;
                if (CRuntime.IsSpace(shown[shownCount]))
                {
                    bool isLineCrossed = false;
                    int next;
                    do
                    {
                        next = VisibleCount;

                        if (length <= next)
                            break;

                        if (shown[next] == CRuntime.LineFeed)
                        {
                            isLineCrossed = true;
                        }

                        VisibleCount = next + 1;
                    }
                    while (CRuntime.IsSpace(shown[next]));

                    if (isLineCrossed)
                    {
                        if (VisibleCount < length)
                        {
                            VisibleCount--;
                        }

                        Timer = LineWait;
                        ChangeAnimation((SpriteAnimation)MenuAnimation.SelectDown);
                        return 0;
                    }
                }

                ChangeAnimation((SpriteAnimation)MenuAnimation.StandUp);
                HasPlayedStateSound = false;
                return 0;
            default:
                return base.Action(act, var1, var2, var3, target);
        }
    }

    [EngineRoutine(0x00448ff0)]
    internal override int Action(int act, ref Resource stream, int version)
    {
        if ((ActionCode)act != ActionCode.Restore)
            return base.Action(act, ref stream, version);

        base.Action(act, ref stream, version);
        var behavior = TextBehavior;
        stream.ReadInto(ref behavior);
        TextBehavior = behavior;
        Map.SetText(this, stream.ReadText().ToArray());

        return 0;
    }

    private static byte GetCharacter(ReadOnlySpan<byte> text, int index) => (uint)index < (uint)text.Length ? text[index] : (byte)0;

    private ReadOnlyMemory<byte> ReadTextAddress(int address) => address == 0 ? default : Map.Script.GetSlot(address - 1).Text;
}
