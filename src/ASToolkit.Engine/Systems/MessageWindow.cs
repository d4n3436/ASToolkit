using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

internal sealed class MessageWindow
{
    private const float ScreenDepth = 2000;
    private const float MarkerIndent = 10;
    private const int MessageSound = 0x6a;
    private const int LineStep = -1;

    private static readonly Vector2 NoTarget = new(-999999f);
    private static readonly Vector2 Unset = new(-1);
    private readonly Map _map;
    private readonly int _lineId;
    private readonly int _markerId;
    private readonly Vector2 _origin;
    private readonly int _count;
    private readonly int _idleTime;
    private readonly Sprite?[] _lines;
    private readonly Sprite?[] _markers;
    private readonly Vector2[] _targets;
    private int _shownAt;

    [EngineRoutine(0x0042e120)]
    public MessageWindow(Map map, int lineId, int markerId, Vector2 origin, int count, int idleTime)
    {
        _map = map;
        _count = count;
        _idleTime = idleTime;
        _lineId = lineId;
        _markerId = markerId;
        _origin = origin;
        _lines = new Sprite?[count + 1];
        _markers = new Sprite?[count + 1];
        _targets = new Vector2[count + 1];
    }

    public void Clear()
    {
        Array.Clear(_lines);
        Array.Clear(_markers);
        Array.Clear(_targets);
        _shownAt = 0;
    }

    [EngineRoutine(0x0042e4a0)]
    public void Put(ReadOnlyMemory<byte> text, Vector2 target)
    {
        int lineHeight = GetLineHeight();

        if (text.IsEmpty)
            return;

        _shownAt = _map.Time;
        if (_lines[_count] is not null)
        {
            Shift();
        }

        float bottom = _map.Session.View.Top + (_count - 1) * lineHeight;
        if (_lines[_count] is null)
        {
            var position = new Vector3(_origin.X, bottom + _origin.Y + ScreenDepth, ScreenDepth);
            _lines[_count] = _map.CreateSprite(FindDefinition(_lineId), position, default);
        }

        var line = _lines[_count];
        if (line is not null)
        {
            _map.SetText(line, text);
        }

        _targets[_count] = target;
        if (target != Unset)
        {
            int halfLine = lineHeight / 2;
            var position = new Vector3(_origin.X - MarkerIndent, bottom + halfLine + _origin.Y + ScreenDepth, ScreenDepth);
            _markers[_count] = _map.CreateSprite(FindDefinition(_markerId), position, default);
        }

        _map.Sound.Play(MessageSound, 0, SoundSystem.FullLoudness);
    }

    [EngineRoutine(0x0042e2c0)]
    public void Tact()
    {
        int lineHeight = GetLineHeight();

        if ((uint)(_map.Time - _shownAt) > (uint)_idleTime)
        {
            Shift();
            _shownAt = _map.Time;
        }

        var clicked = _map.MenuLeftClick;

        if (clicked is null || (int)clicked.Definition.Id != _markerId)
            return;

        int i = (int)(clicked.Position.Y - clicked.Position.Z - _map.Camera.Y - _map.Session.View.Top) / lineHeight + 1;

        if (i < 0 || i > _count)
            return;

        if (_targets[i] != NoTarget)
        {
            _map.SetPosition(_targets[i], CameraMove.Smooth);
        }
    }

    private SpriteDefinition FindDefinition(int id) => _map.Definitions.Find((KnownObject)id) ?? _map.Definitions.Empty;

    private int GetLineHeight() => FindDefinition(_lineId).FrameSize.Height + 1;

    [EngineRoutine(0x0042e6a0)]
    private void Shift()
    {
        int lineHeight = GetLineHeight();
        Remove(_lines, 1);
        Remove(_markers, 1);
        for (int i = 2; i <= _count; i++)
        {
            var marker = _markers[i];

            if (marker is null)
                continue;

            MoveOneLine(marker, lineHeight);
            _markers[i - 1] = marker;
            _targets[i - 1] = _targets[i];
            _markers[i] = null;
        }

        for (int i = 2; i <= _count; i++)
        {
            var line = _lines[i];

            if (line is null)
                continue;

            MoveOneLine(line, lineHeight);
            _lines[i - 1] = line;
            _lines[i] = null;
        }
    }

    private void Remove(Sprite?[] sprites, int index)
    {
        var sprite = sprites[index];
        if (sprite is not null)
        {
            _map.DeleteSprite(sprite);
        }

        sprites[index] = null;
    }

    private void MoveOneLine(Sprite sprite, int lineHeight) =>
        sprite.ChangeCoor(sprite.Position with { Y = LineStep * lineHeight + sprite.Position.Y });
}
