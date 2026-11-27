using System.Drawing;
using ASToolkit.Engine;
using ASToolkit.Formats;
using SDL;
using static SDL.SDL3;

namespace ASToolkit.Game;

internal sealed unsafe class SdlWindow : IWindowDevice, IDisposable
{
    private const int WheelDelta = 120;
    private const int WordShift = 16;
    private const int WordMask = 0xFFFF;

    private static readonly WindowMessage[] ActivatedMessages = [new(WindowMessageId.ActivateApp, 1, 0)];
    private static readonly WindowMessage[] PaintMessages = [new(WindowMessageId.Paint, 0, 0)];

    private readonly Queue<WindowMessage> _messages = [];
    private bool _isActive;
    private bool _isAltHeld;
    private bool _isControlHeld;
    private int _cursorCount;

    public SDL_Window* Handle { get; private set; }

    public SDL_Renderer* Renderer { get; set; }

    public bool IsCursorShown => _cursorCount >= 0;

    public Size DesktopSize
    {
        get
        {
            var mode = SDL_GetDesktopDisplayMode(SDL_GetPrimaryDisplay());
            return mode == null ? Size.Empty : new Size(mode->w, mode->h);
        }
    }

    public Rectangle WindowRect
    {
        get
        {
            int x;
            int y;
            int width;
            int height;
            SDL_GetWindowPosition(Handle, &x, &y);
            SDL_GetWindowSize(Handle, &width, &height);

            return new Rectangle(x, y, width, height);
        }
    }

    public Rectangle ClientRect => WindowRect;

    public void CreateWindowEx(ExtendedWindowStyles extendedStyle, ReadOnlySpan<byte> title, WindowStyles style, Rectangle bounds)
    {
        Handle = SDL_CreateWindow(GameTextEncoding.Cyrillic.GetString(title), bounds.Width, bounds.Height, SDL_WindowFlags.SDL_WINDOW_BORDERLESS | SDL_WindowFlags.SDL_WINDOW_HIDDEN);
        if (Handle == null)
            throw new InvalidOperationException($"Could not create the window: {SDL_GetError()}");

        SDL_SetWindowPosition(Handle, bounds.X, bounds.Y);
        SDL_HideCursor();
        SDL_StartTextInput(Handle);
        if (style.HasFlag(WindowStyles.Visible))
        {
            SDL_ShowWindow(Handle);
        }
    }

    public ReadOnlySpan<WindowMessage> ShowWindow()
    {
        SDL_ShowWindow(Handle);
        if (_isActive)
            return [];

        _isActive = true;

        return ActivatedMessages;
    }

    public ReadOnlySpan<WindowMessage> UpdateWindow()
    {
        SDL_PumpEvents();
        if (!SDL_HasEvent(SDL_EventType.SDL_EVENT_WINDOW_EXPOSED))
            return [];

        SDL_FlushEvent(SDL_EventType.SDL_EVENT_WINDOW_EXPOSED);

        return PaintMessages;
    }

    public void ShowCursor(bool show) => _cursorCount += show ? 1 : -1;

    public bool TryPeekMessage(out WindowMessage message)
    {
        SDL_Event sdlEvent;
        while (_messages.Count == 0 && SDL_PollEvent(&sdlEvent))
        {
            if (Renderer != null)
            {
                SDL_ConvertEventToRenderCoordinates(Renderer, &sdlEvent);
            }

            Translate(sdlEvent);
        }

        return _messages.TryDequeue(out message);
    }

    public void Dispose()
    {
        if (Handle == null)
            return;

        SDL_DestroyWindow(Handle);
        Handle = null;
    }

    private static int Pack(Point point) => (point.Y & WordMask) << WordShift | (point.X & WordMask);

    private static VirtualKey ToVirtualKey(SDL_Scancode scancode) => scancode switch
    {
        >= SDL_Scancode.SDL_SCANCODE_A and <= SDL_Scancode.SDL_SCANCODE_Z => VirtualKey.A + (scancode - SDL_Scancode.SDL_SCANCODE_A),
        >= SDL_Scancode.SDL_SCANCODE_1 and <= SDL_Scancode.SDL_SCANCODE_9 => VirtualKey.Number1 + (scancode - SDL_Scancode.SDL_SCANCODE_1),
        SDL_Scancode.SDL_SCANCODE_0 => VirtualKey.Number0,
        >= SDL_Scancode.SDL_SCANCODE_F1 and <= SDL_Scancode.SDL_SCANCODE_F12 => VirtualKey.F1 + (scancode - SDL_Scancode.SDL_SCANCODE_F1),
        >= SDL_Scancode.SDL_SCANCODE_KP_1 and <= SDL_Scancode.SDL_SCANCODE_KP_9 => VirtualKey.Numpad1 + (scancode - SDL_Scancode.SDL_SCANCODE_KP_1),
        SDL_Scancode.SDL_SCANCODE_KP_0 => VirtualKey.Numpad0,
        SDL_Scancode.SDL_SCANCODE_RETURN or SDL_Scancode.SDL_SCANCODE_KP_ENTER => VirtualKey.Enter,
        SDL_Scancode.SDL_SCANCODE_ESCAPE => VirtualKey.Escape,
        SDL_Scancode.SDL_SCANCODE_BACKSPACE => VirtualKey.Backspace,
        SDL_Scancode.SDL_SCANCODE_TAB => VirtualKey.Tab,
        SDL_Scancode.SDL_SCANCODE_SPACE => VirtualKey.Space,
        SDL_Scancode.SDL_SCANCODE_MINUS => VirtualKey.Minus,
        SDL_Scancode.SDL_SCANCODE_EQUALS => VirtualKey.Plus,
        SDL_Scancode.SDL_SCANCODE_LEFTBRACKET => VirtualKey.LeftBracket,
        SDL_Scancode.SDL_SCANCODE_RIGHTBRACKET => VirtualKey.RightBracket,
        SDL_Scancode.SDL_SCANCODE_BACKSLASH => VirtualKey.Backslash,
        SDL_Scancode.SDL_SCANCODE_SEMICOLON => VirtualKey.Semicolon,
        SDL_Scancode.SDL_SCANCODE_APOSTROPHE => VirtualKey.Quote,
        SDL_Scancode.SDL_SCANCODE_GRAVE => VirtualKey.Backquote,
        SDL_Scancode.SDL_SCANCODE_COMMA => VirtualKey.Comma,
        SDL_Scancode.SDL_SCANCODE_PERIOD => VirtualKey.Period,
        SDL_Scancode.SDL_SCANCODE_SLASH => VirtualKey.Slash,
        SDL_Scancode.SDL_SCANCODE_CAPSLOCK => VirtualKey.CapsLock,
        SDL_Scancode.SDL_SCANCODE_PRINTSCREEN => VirtualKey.PrintScreen,
        SDL_Scancode.SDL_SCANCODE_SCROLLLOCK => VirtualKey.ScrollLock,
        SDL_Scancode.SDL_SCANCODE_PAUSE => VirtualKey.Pause,
        SDL_Scancode.SDL_SCANCODE_INSERT => VirtualKey.Insert,
        SDL_Scancode.SDL_SCANCODE_HOME => VirtualKey.Home,
        SDL_Scancode.SDL_SCANCODE_PAGEUP => VirtualKey.PageUp,
        SDL_Scancode.SDL_SCANCODE_DELETE => VirtualKey.Delete,
        SDL_Scancode.SDL_SCANCODE_END => VirtualKey.End,
        SDL_Scancode.SDL_SCANCODE_PAGEDOWN => VirtualKey.PageDown,
        SDL_Scancode.SDL_SCANCODE_RIGHT => VirtualKey.Right,
        SDL_Scancode.SDL_SCANCODE_LEFT => VirtualKey.Left,
        SDL_Scancode.SDL_SCANCODE_DOWN => VirtualKey.Down,
        SDL_Scancode.SDL_SCANCODE_UP => VirtualKey.Up,
        SDL_Scancode.SDL_SCANCODE_NUMLOCKCLEAR => VirtualKey.NumLock,
        SDL_Scancode.SDL_SCANCODE_KP_DIVIDE => VirtualKey.Divide,
        SDL_Scancode.SDL_SCANCODE_KP_MULTIPLY => VirtualKey.Multiply,
        SDL_Scancode.SDL_SCANCODE_KP_MINUS => VirtualKey.Subtract,
        SDL_Scancode.SDL_SCANCODE_KP_PLUS => VirtualKey.Add,
        SDL_Scancode.SDL_SCANCODE_KP_PERIOD => VirtualKey.Decimal,
        SDL_Scancode.SDL_SCANCODE_APPLICATION => VirtualKey.Applications,
        SDL_Scancode.SDL_SCANCODE_LCTRL or SDL_Scancode.SDL_SCANCODE_RCTRL => VirtualKey.Control,
        SDL_Scancode.SDL_SCANCODE_LSHIFT or SDL_Scancode.SDL_SCANCODE_RSHIFT => VirtualKey.Shift,
        SDL_Scancode.SDL_SCANCODE_LALT or SDL_Scancode.SDL_SCANCODE_RALT => VirtualKey.Alt,
        SDL_Scancode.SDL_SCANCODE_LGUI => VirtualKey.LeftWindows,
        SDL_Scancode.SDL_SCANCODE_RGUI => VirtualKey.RightWindows,
        _ => VirtualKey.None
    };

    private static int GetControlCharacter(VirtualKey key, bool isControlHeld) => key switch
    {
        VirtualKey.Backspace or VirtualKey.Tab or VirtualKey.Enter or VirtualKey.Escape => (int)key,
        >= VirtualKey.A and <= VirtualKey.Z when isControlHeld => key - VirtualKey.A + 1,
        _ => 0
    };

    private void Activate(bool isActive)
    {
        if (isActive == _isActive)
            return;

        _isActive = isActive;
        Post(WindowMessageId.ActivateApp, isActive ? 1 : 0, 0);
    }

    private void Post(WindowMessageId message, int wParam, int lParam) => _messages.Enqueue(new WindowMessage(message, wParam, lParam));

    private void Translate(SDL_Event sdlEvent)
    {
        switch (sdlEvent.Type)
        {
            case SDL_EventType.SDL_EVENT_QUIT or SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED:
                Post(WindowMessageId.Close, 0, 0);
                break;
            case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_GAINED:
                Activate(true);
                break;
            case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_LOST:
                Activate(false);
                break;
            case SDL_EventType.SDL_EVENT_WINDOW_MOVED:
                Post(WindowMessageId.Move, 0, Pack(new Point(sdlEvent.window.data1, sdlEvent.window.data2)));
                break;
            case SDL_EventType.SDL_EVENT_WINDOW_EXPOSED:
                Post(WindowMessageId.Paint, 0, 0);
                break;
            case SDL_EventType.SDL_EVENT_KEY_DOWN or SDL_EventType.SDL_EVENT_KEY_UP:
                PostKey(sdlEvent.key);
                break;
            case SDL_EventType.SDL_EVENT_TEXT_INPUT:
                foreach (byte character in GameTextEncoding.Cyrillic.GetBytes(sdlEvent.text.GetText() ?? string.Empty))
                {
                    Post(WindowMessageId.Char, character, 0);
                }

                break;
            case SDL_EventType.SDL_EVENT_MOUSE_MOTION:
                PostHitTest(sdlEvent.motion.x, sdlEvent.motion.y);
                break;
            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN or SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
                var client = PostHitTest(sdlEvent.button.x, sdlEvent.button.y);
                bool isDown = sdlEvent.button.down;
                WindowMessageId? button = sdlEvent.button.button switch
                {
                    SDL_BUTTON_LEFT => isDown ? WindowMessageId.LeftButtonDown : WindowMessageId.LeftButtonUp,
                    SDL_BUTTON_RIGHT => isDown ? WindowMessageId.RightButtonDown : WindowMessageId.RightButtonUp,
                    SDL_BUTTON_MIDDLE when isDown => WindowMessageId.MiddleButtonDown,
                    _ => null
                };
                if (button is not null)
                {
                    Post(button.Value, 0, Pack(client));
                }

                break;
            case SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
                int steps = sdlEvent.wheel.direction == SDL_MouseWheelDirection.SDL_MOUSEWHEEL_FLIPPED ? -sdlEvent.wheel.integer_y : sdlEvent.wheel.integer_y;
                if (steps == 0)
                    break;

                var wheelPoint = PostHitTest(sdlEvent.wheel.mouse_x, sdlEvent.wheel.mouse_y);
                int delta = steps * WheelDelta;
                Post(WindowMessageId.MouseWheel, (delta & WordMask) << WordShift, Pack(wheelPoint + new Size(WindowRect.Location)));
                break;
        }
    }

    private void PostKey(in SDL_KeyboardEvent key)
    {
        var virtualKey = ToVirtualKey(key.scancode);
        if (virtualKey == VirtualKey.None)
            return;

        bool isDown = key.down;
        if (virtualKey == VirtualKey.Alt)
        {
            _isAltHeld = isDown;
        }
        else if (virtualKey == VirtualKey.Control)
        {
            _isControlHeld = isDown;
        }

        bool isSystem = _isAltHeld || virtualKey is VirtualKey.Alt or VirtualKey.F10;
        var message = isDown ? isSystem ? WindowMessageId.SysKeyDown : WindowMessageId.KeyDown : isSystem ? WindowMessageId.SysKeyUp : WindowMessageId.KeyUp;
        Post(message, (int)virtualKey, 0);
        int character = isDown && !isSystem ? GetControlCharacter(virtualKey, _isControlHeld) : 0;
        if (character != 0)
        {
            Post(WindowMessageId.Char, character, 0);
        }
    }

    private Point PostHitTest(float x, float y)
    {
        var client = new Point((int)x, (int)y);
        Post(WindowMessageId.HitTest, 0, Pack(client + new Size(WindowRect.Location)));

        return client;
    }
}
