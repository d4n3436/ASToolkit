using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal sealed class MessageWindow
{
    private const float ScreenDepth = 2000;
    private const float MarkerIndent = 10;
    private const int MessageSound = 0x6a;
    private static readonly Vector2 NoTarget = new(-999999f);
    private static readonly Vector2 Unset = new(-1);
    private readonly Map _map;
    private readonly int _lineId;
    private readonly int _markerId;
    private readonly Vector2 _origin;
    private readonly int _count;
    private readonly int _idleTime;
    private readonly int _step;
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
        _step = -1;
        _lines = new Sprite?[count + 1];
        _markers = new Sprite?[count + 1];
        _targets = new Vector2[count + 1];
    }

    private SpriteDefinition FindDefinition(int id) => _map.Definitions.Find((KnownObject)id) ?? _map.Definitions.Empty;

    private int GetLineHeight() => FindDefinition(_lineId).FrameHeight + 1;

    [EngineRoutine(0x0042e4a0)]
    public void Add(ReadOnlyMemory<byte> text, Vector2 target)
    {
        int lineHeight = GetLineHeight();

        if (text.IsEmpty)
            return;

        _shownAt = _map.Time;
        if (_lines[_count] is not null)
        {
            Scroll();
        }

        double bottom = (double)_map.Session.View.Top + (_count - 1) * lineHeight;
        if (_lines[_count] is null)
        {
            var position = new Vector3(_origin.X, (float)(bottom + _origin.Y + ScreenDepth), ScreenDepth);
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
            var position = new Vector3((float)((double)_origin.X - MarkerIndent), (float)(bottom + halfLine + _origin.Y + ScreenDepth), ScreenDepth);
            _markers[_count] = _map.CreateSprite(FindDefinition(_markerId), position, default);
        }

        _map.Sound.Play(MessageSound, 0, SoundSystem.FullLoudness);
    }

    [EngineRoutine(0x0042e6a0)]
    private void Scroll()
    {
        int lineHeight = GetLineHeight();
        Remove(_lines, 1);
        Remove(_markers, 1);
        for (int index = 2; index <= _count; index++)
        {
            var marker = _markers[index];

            if (marker is null)
                continue;

            Shift(marker, lineHeight);
            _markers[index - 1] = marker;
            _targets[index - 1] = _targets[index];
            _markers[index] = null;
        }

        for (int index = 2; index <= _count; index++)
        {
            var line = _lines[index];

            if (line is null)
                continue;

            Shift(line, lineHeight);
            _lines[index - 1] = line;
            _lines[index] = null;
        }
    }

    private void Remove(Sprite?[] sprites, int index)
    {
        var sprite = sprites[index];
        if (sprite is not null)
        {
            _map.DestroySprite(sprite);
        }

        sprites[index] = null;
    }

    private void Shift(Sprite sprite, int lineHeight) =>
        sprite.ChangeCoor(sprite.Position with { Y = _step * lineHeight + sprite.Position.Y });

    // Leaves out the delayed message queue (+0x2f8, +0x300), which no routine of the game fills
    [EngineRoutine(0x0042e2c0)]
    public void Update()
    {
        int lineHeight = GetLineHeight();

        if ((uint)(_map.Time - _shownAt) > (uint)_idleTime)
        {
            Scroll();
            _shownAt = _map.Time;
        }

        var clicked = _map.MenuLeftClick;

        if (clicked is null || (int)clicked.Definition.Id != _markerId)
            return;

        int index = (int)((double)clicked.Position.Y - clicked.Position.Z - _map.Camera.Y - _map.Session.View.Top) / lineHeight + 1;

        if (index < 0 || index > _count)
            return;

        if (_targets[index] != NoTarget)
        {
            _map.SetPosition(_targets[index], CameraMove.Smooth);
        }
    }
}
