using System.Drawing;
using System.Runtime.InteropServices;
using ASToolkit.Audio;
using ASToolkit.Engine;
using ASToolkit.Engine.IO;
using SDL;
using static SDL.SDL3;

namespace ASToolkit.Game;

internal static class Program
{
    private static readonly Size[] AcceptedSizes =
    [
        new(640, 480), new(800, 600), new(1024, 768), new(1280, 720), new(1280, 800), new(1280, 1024),
        new(1366, 768), new(1440, 900), new(1600, 900), new(1680, 1050), new(1920, 1080)
    ];

    private static readonly int[] AcceptedBitDepths = [32];

    private static volatile bool _isQuitRequested;

    private static void Main(string[] args)
    {
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, RequestQuit);
        using var termination = PosixSignalRegistration.Create(PosixSignal.SIGTERM, RequestQuit);
        using var hangUp = PosixSignalRegistration.Create(PosixSignal.SIGHUP, RequestQuit);
        string folder = Environment.CurrentDirectory;
        var fileSystem = new GameFolder(folder);
        string programPath = Environment.ProcessPath?.Replace(Path.DirectorySeparatorChar, '\\') ?? throw new InvalidOperationException("Could not find the path of the executable.");

        SDL_SetHint(SDL_HINT_QUIT_ON_LAST_WINDOW_CLOSE, "0"u8);
        if (!SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO))
            throw new InvalidOperationException($"Could not start SDL: {SDL_GetError()}");

        try
        {
            using var window = new SdlWindow();
            using var device = new SdlGraphicsDevice(window);
            using var cursor = new SdlCursor();
            using var session = new GameSession(fileSystem, programPath, SdlGraphicsDevice.ReadAdapterModes(), new RegistryFile(fileSystem), TimeProvider.System);
            session.Shell = new ProcessShellDevice();
            session.Window = window;
            session.GraphicsDevice = device;
            
            var init = new GraphicsInit(AcceptedSizes, AcceptedBitDepths, new Size(1280, 720), 32, true);
            using var map = new MapSteam(session, init, string.Join(' ', args));
            using var sound = new SoundOutput(folder, map.Objects.Sounds);
            if (!map.IsInitialized)
                return;
            
            while (!map.Tact())
            {
                cursor.Update(map.Mouse!, window.IsCursorShown);
                sound.Sync(session.Sound!, map.RealCurrentTime);
                if (_isQuitRequested)
                {
                    _isQuitRequested = false;
                    map.Input.PostMessage(WindowMessageId.Close, 0, 0);
                }
            }
        }
        finally
        {
            SDL_Quit();
        }
    }

    private static void RequestQuit(PosixSignalContext context)
    {
        context.Cancel = true;
        _isQuitRequested = true;
    }
}
