using System.Numerics;
using System.Text.Unicode;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public sealed partial class Map
{
    private const KnownObject MouseTipFont = (KnownObject)6;
    private const int MouseTipRecheck = 500;
    private const int MouseTipFrameLength = 2;
    private const int MouseTipKeyLength = 32;
    private const float MouseTipOffsetX = 5;
    private const float MouseTipDepth = 3000;
    private const float MouseTipRaise = 10;
    private const float MouseTipDrop = 3010;
    private const float MouseTipHalf = 0.5f;
    private const byte MouseTipOpen = (byte)'{';
    private const byte MouseTipClose = (byte)'}';

    private Sprite? _mouseTip;

    private static ReadOnlySpan<byte> MouseTipsSection => "MouseTips"u8;

    [EngineRoutine(0x0043da80)]
    private void TactMouseTip(PlayerInput input)
    {
        uint now = WallTime;
        var cursor = input.ScreenCursor;

        if (Session.MouseTipCursor != cursor)
        {
            Session.MouseTipMovedAt = now;
            Session.MouseTipCursor = cursor;
        }

        bool isResting = now - Session.MouseTipMovedAt > (uint)Definitions.HoverDelay && !input.Flags.HasFlag(InputFlags.LeftClick) && input.Key == 0 &&
            !_menuClicks.HasFlag(MenuClicks.Left) && _flags.HasFlag(MapFlags.MouseTips);

        if (!isResting)
        {
            DeleteMouseTip();
            return;
        }

        if (_mouseTip is not null)
        {
            if (now - Session.MouseTipMovedAt <= (uint)Definitions.HoverDelay + MouseTipRecheck)
                return;

            Session.MouseTipMovedAt += MouseTipRecheck;

            if (GetMouseTipText().Span.SequenceEqual(Session.MouseTipText.Span))
                return;

            DeleteMouseTip();
            return;
        }

        var font = Definitions.Find(MouseTipFont) ?? Definitions.Empty;
        Session.MouseTipText = GetMouseTipText();

        if (font == Definitions.Empty || Session.MouseTipText.IsEmpty)
            return;

        var size = font.Object.Size;
        var view = Session.Graphics.Viewport;
        var position = new Vector2(cursor.X + MouseTipOffsetX, cursor.Y - size.Y + MouseTipDepth - MouseTipRaise);
        float half = size.Y * MouseTipHalf;

        if (view.Top >= half + position.Y - MouseTipDepth)
        {
            position.Y = half + cursor.Y + MouseTipDrop;
        }

        int length = Session.MouseTipText.Length + MouseTipFrameLength;

        if (!(view.Right > length * size.X + position.X))
        {
            position.X = view.Right - length * size.X;
        }

        _mouseTip = AddSprite(font, new Vector3(position, MouseTipDepth), default);

        if (_mouseTip is not Text)
            return;

        byte[] framed = new byte[length];
        framed[0] = MouseTipOpen;
        Session.MouseTipText.Span.CopyTo(framed.AsSpan(1));
        framed[^1] = MouseTipClose;
        SetText((Text)_mouseTip, framed);
    }

    [EngineRoutine(0x0040f460)]
    private ReadOnlyMemory<byte> GetMouseTipText()
    {
        if (MenuCurrent is null)
            return default;

        int id = GetMenuIdUnderCursor();
        Span<byte> key = stackalloc byte[MouseTipKeyLength];
        Utf8.TryWrite(key, $"MenuVid{id}AllDir", out int written);
        var text = ProfileFile.GetString(_fileSystem, StringsFile, MouseTipsSection, key[..written]);

        if (!text.IsEmpty)
            return text;

        Utf8.TryWrite(key, $"MenuVid{id}Dir{GetMenuDirectionUnderCursor()}", out written);

        return ProfileFile.GetString(_fileSystem, StringsFile, MouseTipsSection, key[..written]);
    }

    [EngineRoutine(0x0043dde0)]
    private void DeleteMouseTip()
    {
        if (_mouseTip is not null)
        {
            DeleteSprite(_mouseTip);
        }

        _mouseTip = null;
    }

    [EngineRoutine(0x0043de00)]
    private void ForgetMouseTip(Sprite removed)
    {
        if (_mouseTip == removed)
        {
            _mouseTip = null;
        }
    }
}
