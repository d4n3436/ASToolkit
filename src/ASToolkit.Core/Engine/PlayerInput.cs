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
    /// Presses a key on a WM_KEYDOWN message.
    /// </summary>
    /// <param name="virtualKey">The virtual key.</param>
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

    /// <summary>
    /// Releases a key on a WM_KEYUP message.
    /// </summary>
    /// <param name="virtualKey">The virtual key.</param>
    [EngineRoutine(0x00425d80)]
    public void ReleaseKey(int virtualKey)
    {
        var flag = GetKeyFlag(virtualKey);

        if (flag != InputFlags.None)
        {
            Flags &= ~flag;
        }
    }

    /// <summary>
    /// Types a character on a WM_CHAR message.
    /// </summary>
    /// <param name="character">The character.</param>
    [EngineRoutine(0x00425d80)]
    public void TypeCharacter(int character) => Key = character & CharacterMask;

    /// <summary>
    /// Presses a mouse button on a WM_LBUTTONDOWN or WM_RBUTTONDOWN message.
    /// </summary>
    /// <param name="button">The button.</param>
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

    /// <summary>
    /// Releases a mouse button on a WM_LBUTTONUP or WM_RBUTTONUP message.
    /// </summary>
    /// <param name="button">The button.</param>
    [EngineRoutine(0x00425d80)]
    public void ReleaseButton(int button)
    {
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
    }

    /// <summary>
    /// Presses the middle mouse button on a WM_MBUTTONDOWN message.
    /// </summary>
    [EngineRoutine(0x00425d80)]
    public void PressMiddleButton() => Flags |= InputFlags.MiddleClick;

    /// <summary>
    /// Turns the mouse wheel on a WM_MOUSEWHEEL message.
    /// </summary>
    /// <param name="delta">The wheel delta of the message.</param>
    [EngineRoutine(0x00425d80)]
    public void TurnWheel(int delta) => Wheel = delta / WheelNotch;

    /// <summary>
    /// Releases the held keys and buttons on a WM_ACTIVATEAPP message that takes the focus away.
    /// </summary>
    [EngineRoutine(0x00425d80)]
    public void LoseFocus() => Flags &= ~LostFocusFlags;

    /// <summary>
    /// Moves the window on a WM_MOVE message.
    /// </summary>
    /// <param name="origin">The window's top-left corner on the screen.</param>
    [EngineRoutine(0x00425d80)]
    public void MoveWindow(Point origin) => WindowOrigin = origin;

    /// <summary>
    /// Moves the cursor to a screen point on a WM_NCHITTEST message and returns whether the map cursor changed.
    /// </summary>
    /// <param name="screen">The mouse position on the screen.</param>
    /// <param name="windowOrigin">The window's top-left corner on the screen.</param>
    /// <param name="view">The visible part of the map.</param>
    /// <param name="camera">The camera position on the map.</param>
    /// <returns><see langword="true"/> when the cursor moved; otherwise, <see langword="false"/>.</returns>
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
