using System.Drawing;
using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents the input the game reads from its window messages in a frame.
/// </summary>
public sealed class PlayerInput
{
    private const InputFlags FrameFlags = InputFlags.LeftClick | InputFlags.MiddleClick | InputFlags.RightClick | InputFlags.LeftClickRelease | InputFlags.RightClickRelease;
    private const InputFlags LostFocusFlags = InputFlags.Shift | InputFlags.Ctrl | InputFlags.Alt;
    private const int WheelNotch = 120;
    private const int KeyShift = 8;
    private const int CharacterMask = 0xFF;
    private const int ShiftKey = 0x10;
    private const int CtrlKey = 0x11;
    private const int WindowMessageMove = 0x3;
    private const int WindowMessageActivateApp = 0x1C;
    private const int WindowMessageHitTest = 0x84;
    private const int WindowMessageKeyDown = 0x100;
    private const int WindowMessageKeyUp = 0x101;
    private const int WindowMessageChar = 0x102;
    private const int WindowMessageLeftButtonDown = 0x201;
    private const int WindowMessageLeftButtonUp = 0x202;
    private const int WindowMessageRightButtonDown = 0x204;
    private const int WindowMessageRightButtonUp = 0x205;
    private const int WindowMessageMiddleButtonDown = 0x207;
    private const int WindowMessageMouseWheel = 0x20A;
    private readonly ControlSettings _settings;

    /// <summary>
    /// Creates the input for a set of controls.
    /// </summary>
    /// <param name="settings">The controls.</param>
    public PlayerInput(ControlSettings settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Gets or sets the input flags.
    /// </summary>
    public InputFlags Flags { get; set; }

    /// <summary>
    /// Gets or sets the wheel notches turned this frame.
    /// </summary>
    public int Wheel { get; set; }

    /// <summary>
    /// Gets or sets the cursor in map coordinates.
    /// </summary>
    public Vector2 Cursor { get; set; }

    /// <summary>
    /// Gets or sets the cursor in screen coordinates.
    /// </summary>
    public Vector2 ScreenCursor { get; set; }

    /// <summary>
    /// Gets or sets the character typed this frame.
    /// </summary>
    public int Key { get; set; }

    /// <summary>
    /// Gets or sets the virtual key pressed this frame.
    /// </summary>
    public int VirtualKey { get; set; }

    /// <summary>
    /// Gets the window's top-left corner on the screen.
    /// </summary>
    public Point WindowOrigin { get; private set; }

    [EngineRoutine(0x00426d70)]
    internal void ClearLeftClick() => ClearClick(InputFlags.LeftClick, ControlSettings.LeftButton);

    [EngineRoutine(0x00426da0)]
    internal void ClearRightClick() => ClearClick(InputFlags.RightClick, ControlSettings.RightButton);

    private void ClearClick(InputFlags click, int button)
    {
        var flags = Flags;
        Flags = flags & ~click;
        if (_settings.First.Primary == button)
        {
            Flags = flags & ~(click | InputFlags.First);
        }

        if (_settings.Second.Primary == button)
        {
            Flags &= ~InputFlags.Second;
        }
    }

    /// <summary>
    /// Clears the input that lasts one frame.
    /// </summary>
    [EngineRoutine(0x00426d40)]
    public void ResetFrame()
    {
        var flags = Flags;
        Flags = flags & ~FrameFlags;
        Key = 0;
        VirtualKey = 0;
        Wheel = 0;

        if (!_settings.IsFirstRepeatedWhileHeld)
        {
            Flags = flags & ~(FrameFlags | InputFlags.First);
        }

        if (!_settings.IsSecondRepeatedWhileHeld)
        {
            Flags &= ~InputFlags.Second;
        }
    }

    /// <summary>
    /// Loads the input of a demo frame.
    /// </summary>
    /// <param name="input">The demo frame's input.</param>
    public void Load(in DemoInput input)
    {
        Flags = input.Flags;
        Wheel = input.Wheel;
        Cursor = input.Cursor;
        ScreenCursor = input.ScreenCursor;
        Key = input.Key;
        VirtualKey = input.VirtualKey;
    }

    /// <summary>
    /// Returns the input as a demo frame stores it.
    /// </summary>
    /// <returns>The demo input.</returns>
    public DemoInput Save() => new(Flags, Wheel, Cursor, ScreenCursor, Key, VirtualKey);

    /// <summary>
    /// Handles a window message and returns whether the cursor lies in the view.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="wParam">The message's first parameter.</param>
    /// <param name="lParam">The message's second parameter.</param>
    /// <param name="windowOrigin">The window's top-left corner on the screen.</param>
    /// <param name="view">The visible part of the map.</param>
    /// <param name="camera">The camera position on the map.</param>
    /// <returns><see langword="true"/> when a WM_NCHITTEST message puts the cursor in the view; otherwise, <see langword="false"/>.</returns>
    [EngineRoutine(0x00425d80)]
    public bool WindowProc(int message, int wParam, int lParam, Point windowOrigin = default, RectangleF view = default, Vector2 camera = default)
    {
        switch (message)
        {
            case WindowMessageMove:
                WindowOrigin = windowOrigin;
                break;
            case WindowMessageActivateApp when wParam == 0:
                Flags &= ~LostFocusFlags;
                break;
            case WindowMessageHitTest:
            {
                WindowOrigin = windowOrigin;
                var point = new Vector2((lParam & 0xFFFF) - windowOrigin.X, ((lParam >> 16) & 0xFFFF) - windowOrigin.Y);
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
            case WindowMessageKeyDown:
            {
                Key = wParam << KeyShift;
                VirtualKey = wParam;
                var flag = GetKeyFlag(wParam);

                if (flag != InputFlags.None)
                {
                    Flags |= flag;
                }

                break;
            }
            case WindowMessageKeyUp:
            {
                var flag = GetKeyFlag(wParam);

                if (flag != InputFlags.None)
                {
                    Flags &= ~flag;
                }

                break;
            }
            case WindowMessageChar:
                Key = wParam & CharacterMask;
                break;
            case WindowMessageLeftButtonDown or WindowMessageRightButtonDown:
            {
                int button = message == WindowMessageLeftButtonDown ? ControlSettings.LeftButton : ControlSettings.RightButton;
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

                break;
            }
            case WindowMessageLeftButtonUp or WindowMessageRightButtonUp:
            {
                int button = message == WindowMessageLeftButtonUp ? ControlSettings.LeftButton : ControlSettings.RightButton;
                var flags = Flags;
                var down = button == ControlSettings.LeftButton ? InputFlags.LeftDown : InputFlags.RightDown;
                var released = button == ControlSettings.LeftButton ? InputFlags.LeftClickRelease : InputFlags.RightClickRelease;
                Flags = (flags & ~down) | released;

                if (_settings.First.Primary == button && _settings.IsFirstRepeatedWhileHeld)
                {
                    Flags = (flags & ~(down | InputFlags.First)) | released;
                }

                if (_settings.Second.Primary == button && _settings.IsSecondRepeatedWhileHeld)
                {
                    Flags &= ~InputFlags.Second;
                }

                break;
            }
            case WindowMessageMiddleButtonDown:
                Flags |= InputFlags.MiddleClick;
                break;
            case WindowMessageMouseWheel:
                Wheel = (short)(wParam >> 16) / WheelNotch;
                break;
        }

        return false;
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
