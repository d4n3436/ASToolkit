using System.Drawing;
using System.Runtime.InteropServices;
using ASToolkit.Engine;
using ASToolkit.Formats;
using SDL;
using static SDL.SDL3;

namespace ASToolkit.Game;

internal sealed unsafe class SdlGraphicsDevice : IGraphicsDevice, IDisposable
{
    private const int PresentFailure = -1;

    private readonly SdlWindow _window;
    private SDL_Renderer* _renderer;
    private SDL_Texture* _texture;
    private Screen? _backBuffer;

    public SdlGraphicsDevice(SdlWindow window) => _window = window;

    public static DisplayMode[] ReadAdapterModes()
    {
        using var modes = SDL_GetFullscreenDisplayModes(SDL_GetPrimaryDisplay());
        if (modes is null)
            return [];

        var displayModes = new DisplayMode[modes.Count];
        for (int i = 0; i < modes.Count; i++)
        {
            var mode = modes[modes.Count - 1 - i];
            var format = mode.format switch
            {
                SDL_PixelFormat.SDL_PIXELFORMAT_XRGB8888 => VidPixelFormat.X8R8G8B8,
                SDL_PixelFormat.SDL_PIXELFORMAT_ARGB8888 => VidPixelFormat.A8R8G8B8,
                SDL_PixelFormat.SDL_PIXELFORMAT_RGB565 => VidPixelFormat.R5G6B5,
                SDL_PixelFormat.SDL_PIXELFORMAT_XRGB1555 => VidPixelFormat.X1R5G5B5,
                _ => VidPixelFormat.None
            };
            displayModes[i] = new DisplayMode(new Size(mode.w, mode.h), format);
        }

        return displayModes;
    }

    public void CreateDevice(Screen backBuffer, bool isWindowed, bool isVSync)
    {
        var size = backBuffer.Size;
        if (!isWindowed)
        {
            SDL_DisplayMode mode;
            if (SDL_GetClosestFullscreenDisplayMode(SDL_GetPrimaryDisplay(), size.Width, size.Height, 0, false, &mode))
            {
                SDL_SetWindowFullscreenMode(_window.Handle, &mode);
            }

            SDL_SetWindowFullscreen(_window.Handle, true);
        }

        _renderer = SDL_CreateRenderer(_window.Handle, (Utf8String)null);
        if (_renderer == null)
            throw new InvalidOperationException($"Could not create the renderer: {SDL_GetError()}");

        SDL_SetRenderVSync(_renderer, isVSync ? 1 : 0);
        SDL_SetRenderLogicalPresentation(_renderer, size.Width, size.Height, SDL_RendererLogicalPresentation.SDL_LOGICAL_PRESENTATION_LETTERBOX);
        var format = backBuffer.IsTrueColor ? SDL_PixelFormat.SDL_PIXELFORMAT_XRGB8888 : backBuffer.Format.IsRgb555 ? SDL_PixelFormat.SDL_PIXELFORMAT_XRGB1555 : SDL_PixelFormat.SDL_PIXELFORMAT_RGB565;
        _texture = SDL_CreateTexture(_renderer, format, SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING, size.Width, size.Height);
        if (_texture == null)
            throw new InvalidOperationException($"Could not create the screen texture: {SDL_GetError()}");

        _backBuffer = backBuffer;
        _window.Renderer = _renderer;
    }

    public int BeginScene() => 0;

    public int EndScene() => 0;

    public int Present(Rectangle source, Rectangle destination)
    {
        var screen = _backBuffer;
        if (screen is null || source.Width <= 0 || source.Height <= 0)
            return 0;

        var pixels = screen.IsTrueColor ? MemoryMarshal.AsBytes(screen.TrueColorPixels) : MemoryMarshal.AsBytes(screen.HighColorPixels);
        int bytesPerPixel = screen.IsTrueColor ? sizeof(uint) : sizeof(ushort);
        int pitch = screen.Pitch * bytesPerPixel;
        var area = new SDL_Rect { x = source.X, y = source.Y, w = source.Width, h = source.Height };
        bool isUpdated;
        fixed (byte* first = pixels)
        {
            isUpdated = SDL_UpdateTexture(_texture, &area, (nint)(first + source.Y * pitch + source.X * bytesPerPixel), pitch);
        }

        var from = new SDL_FRect { x = source.X, y = source.Y, w = source.Width, h = source.Height };
        var to = new SDL_FRect { x = destination.X, y = destination.Y, w = destination.Width, h = destination.Height };
        SDL_RenderClear(_renderer);
        bool isDrawn = SDL_RenderTexture(_renderer, _texture, &from, &to);
        bool isPresented = SDL_RenderPresent(_renderer);

        return isUpdated && isDrawn && isPresented ? 0 : PresentFailure;
    }

    public void Dispose()
    {
        _window.Renderer = null;
        if (_texture != null)
        {
            SDL_DestroyTexture(_texture);
        }

        if (_renderer != null)
        {
            SDL_DestroyRenderer(_renderer);
        }
    }
}
