using System.Drawing;
using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the keyboard and mouse input of a frame.
/// </summary>
public sealed class Input
{
    private const InputFlags FrameFlags = InputFlags.LeftClick | InputFlags.MiddleClick | InputFlags.RightClick | InputFlags.LeftClickRelease | InputFlags.RightClickRelease;
    private const InputFlags LostFocusFlags = InputFlags.Shift | InputFlags.Ctrl | InputFlags.Alt;
    private const int WheelNotch = 120;
    private const int KeyShift = 8;
    private const int CharacterMask = 0xFF;

    private readonly Map _map;
    private readonly Queue<WindowMessage> _messages = [];
    private readonly HashSet<VirtualKey> _heldKeys = [];
    private bool _isQuitPosted;

    /// <summary>
    /// Initializes a new instance of the <see cref="Input"/> class.
    /// </summary>
    /// <param name="map">The map whose cursor and view the input moves.</param>
    public Input(Map map)
    {
        _map = map;
    }

    /// <summary>
    /// Gets the input flags.
    /// </summary>
    public InputFlags Flags { get; private set; }

    /// <summary>
    /// Gets the wheel notches turned this frame.
    /// </summary>
    public int Wheel { get; private set; }

    /// <summary>
    /// Gets the cursor in map coordinates.
    /// </summary>
    public Vector2 Cursor { get; internal set; }

    /// <summary>
    /// Gets the cursor in screen coordinates.
    /// </summary>
    public Vector2 ScreenCursor { get; private set; }

    /// <summary>
    /// Gets the character typed this frame.
    /// </summary>
    public int Key { get; private set; }

    /// <summary>
    /// Gets the virtual key pressed this frame.
    /// </summary>
    public VirtualKey VirtualKey { get; private set; }

    /// <summary>
    /// Gets the window's top-left corner on the screen.
    /// </summary>
    public Point WindowOrigin { get; private set; }

    private ControlSettings Settings => _map.Session.Controls!;

    /// <summary>
    /// Returns the input as a demo frame.
    /// </summary>
    /// <returns>The demo input.</returns>
    [EngineRoutine(0x00426330)]
    public DemoInput Save() => new(Flags, Wheel, Cursor, ScreenCursor, Key, (int)VirtualKey);

    /// <summary>
    /// Loads the input of a demo frame.
    /// </summary>
    /// <param name="input">The demo frame's input.</param>
    [EngineRoutine(0x00426350)]
    public void Load(in DemoInput input)
    {
        Flags = input.Flags;
        Wheel = input.Wheel;
        Cursor = input.Cursor;
        ScreenCursor = input.ScreenCursor;
        Key = input.Key;
        VirtualKey = (VirtualKey)input.VirtualKey;
    }

    /// <summary>
    /// Posts a window message for the next frame.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="wParam">The message's first parameter.</param>
    /// <param name="lParam">The message's second parameter.</param>
    public void PostMessage(WindowMessageId message, int wParam, int lParam) => _messages.Enqueue(new WindowMessage(message, wParam, lParam));

    /// <summary>
    /// Posts the key-down and character messages of a key press.
    /// </summary>
    /// <param name="virtualKey">The virtual key code of the key.</param>
    /// <param name="character">The character the key types.</param>
    public void PostKey(VirtualKey virtualKey, int character)
    {
        PostMessage(WindowMessageId.KeyDown, (int)virtualKey, 0);
        PostMessage(WindowMessageId.Char, character, 0);
    }

    /// <summary>
    /// Clears the input that lasts one frame.
    /// </summary>
    [EngineRoutine(0x00426d40)]
    public void Tact()
    {
        var flags = Flags;
        Flags = flags & ~FrameFlags;
        Key = 0;
        VirtualKey = 0;
        Wheel = 0;

        if (!Settings.IsFirstRepeatedWhileHeld)
        {
            Flags = flags & ~(FrameFlags | InputFlags.First);
        }

        if (!Settings.IsSecondRepeatedWhileHeld)
        {
            Flags &= ~InputFlags.Second;
        }
    }

    [EngineRoutine(0x00425d80)]
    internal bool WindowProc(WindowMessageId message, int wParam, int lParam)
    {
        var windowOrigin = _map.Session.Graphics.WindowPosition;
        var view = _map.Session.Graphics.Viewport;
        var shift = _map.Shift;
        var cursorSprite = _map.Mouse;
        InputFlags flag;
        switch (message)
        {
            case WindowMessageId.Move:
                WindowOrigin = windowOrigin;
                break;
            case WindowMessageId.ActivateApp when wParam == 0:
                Flags &= ~LostFocusFlags;
                break;
            case WindowMessageId.HitTest:
                WindowOrigin = windowOrigin;
                var point = new Vector2((lParam & 0xFFFF) - windowOrigin.X, ((lParam >> 16) & 0xFFFF) - windowOrigin.Y);
                ScreenCursor = point;
                var clamped = point;

                if (clamped.X < view.Left)
                {
                    clamped.X = view.Left;
                }

                if (view.Right <= clamped.X)
                {
                    clamped.X = view.Right - 1;
                }

                if (clamped.Y < view.Top)
                {
                    clamped.Y = view.Top;
                }

                if (view.Bottom <= clamped.Y)
                {
                    clamped.Y = view.Bottom - 1;
                }

                Cursor = clamped + shift;
                cursorSprite!.ChangeCoor(new Vector3(Cursor, cursorSprite.Position.Z));

                return view.Left <= point.X && point.X < view.Right && view.Top <= point.Y && point.Y < view.Bottom;
            case WindowMessageId.KeyDown:
                Key = wParam << KeyShift;
                VirtualKey = (VirtualKey)wParam;
                flag = GetKeyFlag(VirtualKey);

                if (flag != InputFlags.None)
                {
                    Flags |= flag;
                }

                break;
            case WindowMessageId.KeyUp:
                flag = GetKeyFlag((VirtualKey)wParam);

                if (flag != InputFlags.None)
                {
                    Flags &= ~flag;
                }

                break;
            case WindowMessageId.Char:
                Key = wParam & CharacterMask;
                break;
            case WindowMessageId.LeftButtonDown or WindowMessageId.RightButtonDown:
                var pressedButton = message == WindowMessageId.LeftButtonDown ? VirtualKey.LeftButton : VirtualKey.RightButton;
                var pressedFlags = Flags;
                var pressed = pressedButton == VirtualKey.LeftButton ? InputFlags.LeftClick | InputFlags.LeftDown : InputFlags.RightClick | InputFlags.RightDown;
                Flags = pressedFlags | pressed;

                if (Settings.First.Primary == pressedButton)
                {
                    Flags = pressedFlags | pressed | InputFlags.First;
                }

                if (Settings.Second.Primary == pressedButton)
                {
                    Flags |= InputFlags.Second;
                }

                break;
            case WindowMessageId.LeftButtonUp or WindowMessageId.RightButtonUp:
                var releasedButton = message == WindowMessageId.LeftButtonUp ? VirtualKey.LeftButton : VirtualKey.RightButton;
                var releasedFlags = Flags;
                var down = releasedButton == VirtualKey.LeftButton ? InputFlags.LeftDown : InputFlags.RightDown;
                var released = releasedButton == VirtualKey.LeftButton ? InputFlags.LeftClickRelease : InputFlags.RightClickRelease;
                Flags = (releasedFlags & ~down) | released;

                if (Settings.First.Primary == releasedButton && Settings.IsFirstRepeatedWhileHeld)
                {
                    Flags = (releasedFlags & ~(down | InputFlags.First)) | released;
                }

                if (Settings.Second.Primary == releasedButton && Settings.IsSecondRepeatedWhileHeld)
                {
                    Flags &= ~InputFlags.Second;
                }

                break;
            case WindowMessageId.MiddleButtonDown:
                Flags |= InputFlags.MiddleClick;
                break;
            case WindowMessageId.MouseWheel:
                Wheel = (short)(wParam >> 16) / WheelNotch;
                break;
        }

        return false;
    }

    internal void PostQuitMessage() => _isQuitPosted = true;

    internal bool IsKeyHeld(VirtualKey virtualKey) => _heldKeys.Contains(virtualKey);

    internal bool TryPeekMessage(IWindowDevice? window, out WindowMessage message)
    {
        if (_messages.TryDequeue(out message) || window is not null && window.TryPeekMessage(out message))
        {
            if (message.Id is WindowMessageId.KeyDown or WindowMessageId.SysKeyDown)
            {
                _heldKeys.Add((VirtualKey)message.WParam);
            }
            else if (message.Id is WindowMessageId.KeyUp or WindowMessageId.SysKeyUp)
            {
                _heldKeys.Remove((VirtualKey)message.WParam);
            }

            return true;
        }

        if (!_isQuitPosted)
            return false;

        _isQuitPosted = false;
        message = new WindowMessage(WindowMessageId.Quit, 0, 0);

        return true;
    }

    [EngineRoutine(0x00426d70)]
    internal void ClearLeftClick() => ClearClick(InputFlags.LeftClick, VirtualKey.LeftButton);

    [EngineRoutine(0x00426da0)]
    internal void ClearRightClick() => ClearClick(InputFlags.RightClick, VirtualKey.RightButton);

    private InputFlags GetKeyFlag(VirtualKey virtualKey)
    {
        if (Settings.Left.IsMatch(virtualKey))
            return InputFlags.Left;

        if (Settings.Right.IsMatch(virtualKey))
            return InputFlags.Right;

        if (Settings.Up.IsMatch(virtualKey))
            return InputFlags.Up;

        if (Settings.Down.IsMatch(virtualKey))
            return InputFlags.Down;

        if (Settings.First.IsMatch(virtualKey))
            return InputFlags.First;

        if (Settings.Second.IsMatch(virtualKey))
            return InputFlags.Second;

        if (virtualKey == VirtualKey.Shift)
            return InputFlags.Shift;

        return virtualKey == VirtualKey.Control ? InputFlags.Ctrl : InputFlags.None;
    }

    private void ClearClick(InputFlags click, VirtualKey button)
    {
        var flags = Flags;
        Flags = flags & ~click;
        if (Settings.First.Primary == button)
        {
            Flags = flags & ~(click | InputFlags.First);
        }

        if (Settings.Second.Primary == button)
        {
            Flags &= ~InputFlags.Second;
        }
    }
}
