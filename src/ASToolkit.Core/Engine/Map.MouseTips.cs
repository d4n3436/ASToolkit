using System.Numerics;
using System.Text.Unicode;
using ASToolkit.Core.Engine.IO;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

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
        var session = Session;
        uint now = WallTime;
        var cursor = input.ScreenCursor;

        if (session.MouseTipCursor != cursor)
        {
            session.MouseTipMovedAt = now;
            session.MouseTipCursor = cursor;
        }

        bool isResting = now - session.MouseTipMovedAt > (uint)Definitions.HoverDelay && !input.Flags.HasFlag(InputFlags.LeftClick) && input.Key == 0 &&
            !_menuClicks.HasFlag(MenuClicks.Left);

        if (!isResting)
        {
            DeleteMouseTip();
            return;
        }

        if (_mouseTip is not null)
        {
            if (now - session.MouseTipMovedAt <= (uint)Definitions.HoverDelay + MouseTipRecheck)
                return;

            session.MouseTipMovedAt += MouseTipRecheck;

            if (GetMouseTipText().Span.SequenceEqual(session.MouseTipText.Span))
                return;

            DeleteMouseTip();
            return;
        }

        var font = Definitions.Find(MouseTipFont) ?? Definitions.Empty;
        session.MouseTipText = GetMouseTipText();

        if (font == Definitions.Empty || session.MouseTipText.IsEmpty)
            return;

        var size = font.Object.Size;
        var view = session.View;
        float x = cursor.X + MouseTipOffsetX;
        float y = cursor.Y - size.Y + MouseTipDepth - MouseTipRaise;
        float half = size.Y * MouseTipHalf;

        if (view.Top >= half + y - MouseTipDepth)
        {
            y = half + cursor.Y + MouseTipDrop;
        }

        int length = session.MouseTipText.Length + MouseTipFrameLength;

        if (!(view.Right > length * size.X + x))
        {
            x = view.Right - length * size.X;
        }

        _mouseTip = CreateSprite(font, new Vector3(x, y, MouseTipDepth), default);

        if (_mouseTip is not TextSprite)
            return;

        byte[] framed = new byte[length];
        framed[0] = MouseTipOpen;
        session.MouseTipText.Span.CopyTo(framed.AsSpan(1));
        framed[^1] = MouseTipClose;
        SetText(_mouseTip, framed);
    }

    [EngineRoutine(0x0040f460)]
    private ReadOnlyMemory<byte> GetMouseTipText()
    {
        if (_menuCurrent is null)
            return ReadOnlyMemory<byte>.Empty;

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
