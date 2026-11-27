using ASToolkit.Engine;
using ASToolkit.Formats;
using SDL;
using static SDL.SDL3;

namespace ASToolkit.Game;

internal sealed unsafe class SdlCursor : IDisposable
{
    private const int NoCursor = -1;

    private readonly Dictionary<int, nint> _cursors = [];
    private int _shown = int.MinValue;

    public void Update(Mouse mouse, bool isShown)
    {
        int shown = isShown ? mouse.ShownCursor : NoCursor;
        if (shown == _shown)
            return;

        _shown = shown;
        var image = mouse.ShownCursorImage;
        if (shown < 0 || !image.IsValid)
        {
            SDL_HideCursor();
            return;
        }

        if (!_cursors.TryGetValue(shown, out nint cursor))
        {
            cursor = (nint)Create(image);
            _cursors[shown] = cursor;
        }

        SDL_SetCursor((SDL_Cursor*)cursor);
        SDL_ShowCursor();
    }

    public void Dispose()
    {
        foreach (nint cursor in _cursors.Values)
        {
            SDL_DestroyCursor((SDL_Cursor*)cursor);
        }

        _cursors.Clear();
    }

    private static SDL_Cursor* Create(CursorImage image)
    {
        var size = image.Size;
        uint[] pixels = new uint[size.Width * size.Height];
        image.CopyTo(pixels);

        fixed (uint* first = pixels)
        {
            var surface = SDL_CreateSurfaceFrom(size.Width, size.Height, SDL_PixelFormat.SDL_PIXELFORMAT_ARGB8888, (nint)first, size.Width * sizeof(uint));
            var cursor = SDL_CreateColorCursor(surface, image.Hotspot.X, image.Hotspot.Y);
            SDL_DestroySurface(surface);

            return cursor;
        }
    }
}
