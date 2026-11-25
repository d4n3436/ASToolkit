using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

internal sealed class MouseTips
{
    private const KnownObject Font = (KnownObject)6;
    private const int Recheck = 500;
    private const int FrameLength = 2;
    private const float OffsetX = 5;
    private const float Depth = 3000;
    private const float Raise = 10;
    private const float Drop = 3010;
    private const float Half = 0.5f;
    private const byte Open = (byte)'{';
    private const byte Close = (byte)'}';

    private readonly Map _map;
    private Sprite? _tip;

    public MouseTips(Map map)
    {
        _map = map;
    }

    [EngineRoutine(0x0043da80)]
    public void Tact(Input input)
    {
        uint realTime = _map.RealCurrentTime;
        var cursor = input.ScreenCursor;

        if (_map.Session.MouseTipCursor != cursor)
        {
            _map.Session.MouseTipMovedAt = realTime;
            _map.Session.MouseTipCursor = cursor;
        }

        bool isResting = realTime - _map.Session.MouseTipMovedAt > (uint)_map.Constant.HoverDelay && !input.Flags.HasFlag(InputFlags.LeftClick) && input.Key == 0 &&
            !_map.Menu.Clicks.HasFlag(MenuClicks.Left) && _map.HasFlag(MapFlags.MouseTips);

        if (!isResting)
        {
            Clear();
            return;
        }

        if (_tip is not null)
        {
            if (realTime - _map.Session.MouseTipMovedAt <= (uint)_map.Constant.HoverDelay + Recheck)
                return;

            _map.Session.MouseTipMovedAt += Recheck;

            if (_map.GetMouseTipsString().Span.SequenceEqual(_map.Session.MouseTipText.Span))
                return;

            Clear();
            return;
        }

        var font = _map.FindVid(Font) ?? _map.EmptyVid;
        _map.Session.MouseTipText = _map.GetMouseTipsString();

        if (font == _map.EmptyVid || _map.Session.MouseTipText.IsEmpty)
            return;

        var size = font.Size;
        var view = _map.Session.Graphics.Viewport;
        var position = new Vector2(cursor.X + OffsetX, cursor.Y - size.Y + Depth - Raise);
        float half = size.Y * Half;

        if (view.Top >= half + position.Y - Depth)
        {
            position.Y = half + cursor.Y + Drop;
        }

        int length = _map.Session.MouseTipText.Length + FrameLength;

        if (!(view.Right > length * size.X + position.X))
        {
            position.X = view.Right - length * size.X;
        }

        _tip = _map.CreateSprite(font, new Vector3(position, Depth), default);

        if (_tip is not SText text)
            return;

        byte[] framed = new byte[length];
        framed[0] = Open;
        _map.Session.MouseTipText.Span.CopyTo(framed.AsSpan(1));
        framed[^1] = Close;
        text.SetText(framed);
    }

    [EngineRoutine(0x0043dde0)]
    public void Clear()
    {
        if (_tip is not null)
        {
            _map.DeleteSprite(_tip);
        }

        _tip = null;
    }

    [EngineRoutine(0x0043de00)]
    public void DeletePointerToSprite(Sprite removed)
    {
        if (_tip == removed)
        {
            _tip = null;
        }
    }
}
