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

    /// <summary>
    /// Initializes a new instance of the <see cref="Text"/> class.
    /// </summary>
    /// <param name="number">The number of the sprite.</param>
    /// <param name="definition">The definition of the sprite.</param>
    /// <param name="position">The position of the sprite.</param>
    /// <param name="army">The army of the sprite.</param>
    /// <param name="hitPoints">The hit points of the sprite.</param>
    /// <param name="direction">The direction of the sprite.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    public Text(int number, SpriteDefinition definition, Vector3 position, Army army, int hitPoints, Angle direction = default, Map? map = null)
        : base(number, definition, position, army, hitPoints, direction, map)
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
    /// Draws the shown text with the frames of the font definition.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="map">The map.</param>
    /// <param name="camera">The top left corner of the view, in map pixels.</param>
    [EngineRoutine(0x00448ba0)]
    public override void Draw(Screen screen, Map map, Vector2 camera)
    {
        if (Definition == map.Definitions.Empty || Definition.Directions < MinDirections)
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
            var size = Definition.Object.Size.AsVector2();
            var half = Definition.Half;
            var pen = new Vector2(
                TextBehavior.HasFlag(TextBehavior.CenterX)
                    ? savedPosition.X - (LineWidth - 1) * size.X * Half
                    : TextBehavior.HasFlag(TextBehavior.Right) ? savedPosition.X - ((LineWidth - 1) * size.X + half.X) : half.X + savedPosition.X,
                TextBehavior.HasFlag(TextBehavior.CenterY)
                    ? savedPosition.Y - (LineCount - 1) * size.Y * Half
                    : TextBehavior.HasFlag(TextBehavior.Bottom) ? savedPosition.Y - ((LineCount - 1) * size.Y + half.Y) : half.Y + savedPosition.Y);
            float lineStart = pen.X - size.X;
            var current = Definition;
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
                    current = map.Definitions.Find((KnownObject)id) ?? map.Definitions.Empty;
                    pen.X -= current.Object.Size.X;
                    int end = shown.IndexOf(TagEnd);
                    i += end < 0 ? shown.Length : end;
                }
                else if (character == FontSwitch && i + 1 < VisibleCount && map.Definitions.Find((KnownObject)shown[i + 1]) is not null)
                {
                    int id = (sbyte)shown[i + 1];
                    i++;
                    current = map.Definitions.Find((KnownObject)id) ?? map.Definitions.Empty;
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

    internal override int Action(Map map, int act, int var1, int var2, int var3, Sprite? target) => map.TextAction(this, act, var1, var2, var3, target);

    private static byte GetCharacter(ReadOnlySpan<byte> text, int index) => (uint)index < (uint)text.Length ? text[index] : (byte)0;
}
