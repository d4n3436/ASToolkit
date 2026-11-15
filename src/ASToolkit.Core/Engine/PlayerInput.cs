using System.Drawing;
using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal sealed class PlayerInput
{
    private const InputFlags FrameFlags = InputFlags.LeftClick | InputFlags.MiddleClick | InputFlags.RightClick | InputFlags.LeftClickRelease | InputFlags.RightClickRelease;
    private const InputFlags LostFocusFlags = InputFlags.Shift | InputFlags.Ctrl | InputFlags.Alt;
    private const int WheelNotch = 120;
    private const int KeyShift = 8;
    private const int CharacterMask = 0xFF;
    private const int ShiftKey = 0x10;
    private const int CtrlKey = 0x11;
    private readonly ControlSettings _settings;

    public PlayerInput(ControlSettings settings)
    {
        _settings = settings;
    }

    public InputFlags Flags { get; set; }

    public int Wheel { get; set; }

    public Vector2 Cursor { get; set; }

    public Vector2 ScreenCursor { get; set; }

    public int Key { get; set; }

    public int VirtualKey { get; set; }

    public Point WindowOrigin { get; private set; }

    [EngineRoutine(0x00426d40)]
    public void ResetFrame()
    {
        var flags = Flags;
        Flags = flags & ~FrameFlags;
        Key = 0;
        VirtualKey = 0;
        Wheel = 0;

        if (!_settings.HoldsFirst)
        {
            Flags = flags & ~(FrameFlags | InputFlags.First);
        }

        if (!_settings.HoldsSecond)
        {
            Flags &= ~InputFlags.Second;
        }
    }

    public void Load(in DemoInput input)
    {
        Flags = input.Flags;
        Wheel = input.Wheel;
        Cursor = input.Cursor;
        ScreenCursor = input.ScreenCursor;
        Key = input.Key;
        VirtualKey = input.VirtualKey;
    }

    public DemoInput Save() => new(Flags, Wheel, Cursor, ScreenCursor, Key, VirtualKey);

    // WM_KEYDOWN
    [EngineRoutine(0x00425d80)]
    public void PressKey(int virtualKey)
    {
        Key = virtualKey << KeyShift;
        VirtualKey = virtualKey;

        var flag = GetKeyFlag(virtualKey);

        if (flag != InputFlags.None)
        {
            Flags |= flag;
        }
    }

    // WM_KEYUP
    [EngineRoutine(0x00425d80)]
    public void ReleaseKey(int virtualKey)
    {
        var flag = GetKeyFlag(virtualKey);

        if (flag != InputFlags.None)
        {
            Flags &= ~flag;
        }
    }

    // WM_CHAR
    [EngineRoutine(0x00425d80)]
    public void TypeCharacter(int character) => Key = character & CharacterMask;

    // WM_LBUTTONDOWN and WM_RBUTTONDOWN
    [EngineRoutine(0x00425d80)]
    public void PressButton(int button)
    {
        var flags = Flags;
        var pressed = button == ControlSettings.LeftButton ? InputFlags.LeftClick | InputFlags.LeftDown : InputFlags.RightClick | InputFlags.RightDown;
        Flags = flags | pressed;

        if (_settings.First.Primary == button)
        {
            Flags = flags | pressed | InputFlags.First;
        }

        if (_settings.Second.Primary == button)
        {
            Flags |= InputFlags.Second;
        }
    }

    // WM_LBUTTONUP and WM_RBUTTONUP
    [EngineRoutine(0x00425d80)]
    public void ReleaseButton(int button)
    {
        var flags = Flags;
        var down = button == ControlSettings.LeftButton ? InputFlags.LeftDown : InputFlags.RightDown;
        var released = button == ControlSettings.LeftButton ? InputFlags.LeftClickRelease : InputFlags.RightClickRelease;
        Flags = (flags & ~down) | released;

        if (_settings.First.Primary == button && _settings.HoldsFirst)
        {
            Flags = (flags & ~(down | InputFlags.First)) | released;
        }

        if (_settings.Second.Primary == button && _settings.HoldsSecond)
        {
            Flags &= ~InputFlags.Second;
        }
    }

    // WM_MBUTTONDOWN
    [EngineRoutine(0x00425d80)]
    public void PressMiddleButton() => Flags |= InputFlags.MiddleClick;

    // WM_MOUSEWHEEL
    [EngineRoutine(0x00425d80)]
    public void TurnWheel(int delta) => Wheel = delta / WheelNotch;

    // WM_ACTIVATEAPP losing the focus
    [EngineRoutine(0x00425d80)]
    public void LoseFocus() => Flags &= ~LostFocusFlags;

    // WM_MOVE
    [EngineRoutine(0x00425d80)]
    public void MoveWindow(Point origin) => WindowOrigin = origin;

    // WM_NCHITTEST
    [EngineRoutine(0x00425d80)]
    public bool MoveMouse(Point screen, Point windowOrigin, RectangleF view, Vector2 camera)
    {
        WindowOrigin = windowOrigin;
        var point = new Vector2(screen.X - windowOrigin.X, screen.Y - windowOrigin.Y);
        ScreenCursor = point;
        float x = point.X;
        float y = point.Y;

        if (x < view.Left)
        {
            x = view.Left;
        }

        if (view.Right <= x)
        {
            x = view.Right - 1;
        }

        if (y < view.Top)
        {
            y = view.Top;
        }

        if (view.Bottom <= y)
        {
            y = view.Bottom - 1;
        }

        Cursor = new Vector2(x + camera.X, y + camera.Y);

        return view.Left <= point.X && point.X < view.Right && view.Top <= point.Y && point.Y < view.Bottom;
    }

    private InputFlags GetKeyFlag(int virtualKey)
    {
        if (_settings.Left.Matches(virtualKey))
            return InputFlags.Left;

        if (_settings.Right.Matches(virtualKey))
            return InputFlags.Right;

        if (_settings.Up.Matches(virtualKey))
            return InputFlags.Up;

        if (_settings.Down.Matches(virtualKey))
            return InputFlags.Down;

        if (_settings.First.Matches(virtualKey))
            return InputFlags.First;

        if (_settings.Second.Matches(virtualKey))
            return InputFlags.Second;

        if (virtualKey == ShiftKey)
            return InputFlags.Shift;

        return virtualKey == CtrlKey ? InputFlags.Ctrl : InputFlags.None;
    }
}
