using System.Drawing;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents one run of the game from startup to exit and the state that persists while levels are loaded and released.
/// </summary>
public sealed class GameSession : IDisposable
{
    /// <summary>
    /// Represents the screen width used when the registry doesn't hold one.
    /// </summary>
    public const int DefaultScreenWidth = 1024;

    /// <summary>
    /// Represents the screen height used when the registry doesn't hold one.
    /// </summary>
    public const int DefaultScreenHeight = 768;
    private const string ScreenWidthValue = "ScreenX";
    private const string ScreenHeightValue = "ScreenY";
    private const string SoundHighQualityValue = "SoundHighQuality";
    private const string FullScreenValue = "FullScreen";
    private const int DefaultFullScreen = 1;
    private const string Program = "AlienShooter.exe";
    private const string ConfigFile = "AlienShooter.cfg";
    private const string SetupFont = "Courier";
    private const string DefaultFont = "Arial";
    private const int DefaultFontWidth = 0;
    private const int DefaultFontHeight = 8;
    private const string LowDetailValue = "LowDetail";
    private const string TripleBufferValue = "TripleBuffer";
    private const string BitsPerPixelValue = "BPP";
    private const string DeviceValue = "Device";
    private const int DefaultBitsPerPixel = 32;
    private const int DefaultTripleBuffer = 1;
    private const int DefaultVSync = 1;

    private static readonly Size SetupFontSize = new(7, 8);

    private readonly Registry _registry;
    private bool _isConfigRead;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameSession"/> class that reads the screen size from the registry and uses the default size if the registry doesn't hold one.
    /// </summary>
    /// <param name="registry">The registry that holds the game's settings.</param>
    /// <param name="fileSystem">The file system to write the log to and read the configuration file from, or <see langword="null"/> to not write logs and read the configuration later with <see cref="ReadConfig"/>.</param>
    /// <param name="time">The clock used for the log's file name and timestamps, or <see langword="null"/> for the system clock.</param>
    /// <param name="desktop">The size of the desktop, or <see langword="null"/> if it is at least as large as the screen.</param>
    /// <param name="windowRect">The bounds of the game window on the desktop, or <see langword="null"/> if it covers the screen.</param>
    /// <param name="clientRect">The bounds of the client area of the game window on the desktop, or <see langword="null"/> if it covers the screen.</param>
    public GameSession(Registry registry, IGameFileSystem? fileSystem = null, TimeProvider? time = null, Size? desktop = null, Rectangle? windowRect = null, Rectangle? clientRect = null)
    {
        Time = time ?? TimeProvider.System;
        Error = new MyError(fileSystem, Time, Program);
        Graphics.Error = Error;
        ScreenSize = new SizeF(registry.GetInt(ScreenWidthValue, DefaultScreenWidth), registry.GetInt(ScreenHeightValue, DefaultScreenHeight));
        IsFullScreen = registry.GetInt(FullScreenValue, DefaultFullScreen) != 0;
        IsSoundHighQuality = registry.GetInt(SoundHighQualityValue, 0) != 0;
        _registry = registry;
        Graphics.SetUpSettings(registry.GetInt(BitsPerPixelValue, DefaultBitsPerPixel) == 32, IsFullScreen);
        var screen = Size.Ceiling(ScreenSize);
        SetUpWindow(desktop ?? screen, windowRect ?? new Rectangle(Point.Empty, screen), clientRect ?? new Rectangle(Point.Empty, screen));

        if (fileSystem is not null)
        {
            ReadConfig(fileSystem);
        }
    }

    /// <summary>
    /// Gets a value indicating whether the game is running in debug mode.
    /// </summary>
    /// <remarks>In debug mode, the loading screen displays each step of a map load.</remarks>
    public bool IsDebugMode { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the map draws its frame rate.
    /// </summary>
    public bool ShouldDrawFps { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the map draws the presentation banner.
    /// </summary>
    public bool ShouldDrawPresentation { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the game window has the application menu.
    /// </summary>
    public bool HasAppMenu { get; init; }

    /// <summary>
    /// Gets a value indicating whether the game is running full screen.
    /// </summary>
    /// <remarks>When running in a window, the screen size is limited to the desktop size.</remarks>
    public bool IsFullScreen { get; }

    /// <summary>
    /// Gets a value indicating whether sound is played in high quality.
    /// </summary>
    public bool IsSoundHighQuality { get; }

    /// <summary>
    /// Gets the size of the screen in pixels.
    /// </summary>
    public SizeF ScreenSize { get; private set; }

    /// <summary>
    /// Gets or sets the speed the camera scrolls at along each axis in pixels per millisecond.
    /// </summary>
    public Vector2 ScrollSpeed { get; set; }

    /// <summary>
    /// Gets or sets the sound effects and music of the session.
    /// </summary>
    public Sound? Sound { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the next map load records the input of every frame into a demo kept in memory.
    /// </summary>
    public bool IsRecordingNextMap { get; set; }

    /// <summary>
    /// Gets the name of the map the session loaded last.
    /// </summary>
    public ReadOnlyMemory<byte> MapName { get; private set; }

    /// <summary>
    /// Gets the name of the map the session loaded before the last one.
    /// </summary>
    public ReadOnlyMemory<byte> PreviousMapName { get; private set; }

    /// <summary>
    /// Gets the graphics state that persists across levels.
    /// </summary>
    public Graphics Graphics { get; } = new();

    internal TimeProvider Time { get; }

    internal MyError Error { get; }

    internal uint MouseTipMovedAt { get; set; }

    internal Vector2 MouseTipCursor { get; set; }

    internal ReadOnlyMemory<byte> MouseTipText { get; set; }

    private static ReadOnlySpan<byte> NonGdiLog => "zm_nongdi"u8;

    private static ReadOnlySpan<byte> GraphicsTag => "GRAPH"u8;

    private static ReadOnlySpan<byte> LightBufferName => "light buffer"u8;

    private static ReadOnlySpan<byte> HighBufferName => "hiBuffer"u8;

    private static ReadOnlySpan<byte> AlphaBufferName => "alphaBuffer"u8;

    private static ReadOnlySpan<byte> GameSection => "game"u8;

    private static ReadOnlySpan<byte> GraphSection => "graph"u8;

    /// <summary>
    /// Reads the session's settings from the game's configuration file.
    /// </summary>
    /// <param name="fileSystem">The file system that holds the configuration file.</param>
    /// <remarks>Calls after the first one do nothing.</remarks>
    public void ReadConfig(IGameFileSystem fileSystem)
    {
        if (_isConfigRead)
            return;

        _isConfigRead = true;
        int height = Profile.GetInteger(fileSystem, ConfigFile, GraphSection, "FontSizeY"u8, DefaultFontHeight);
        int width = Profile.GetInteger(fileSystem, ConfigFile, GraphSection, "FontSizeX"u8, DefaultFontWidth);
        string font = Profile.TryGetString(fileSystem, ConfigFile, GraphSection, "Font"u8, out var name) ? Encoding.ASCII.GetString(name.Span) : DefaultFont;
        Graphics.CreateDeviceFont(font, new Size(width, height), SpriteCatalog.DefaultMaxTextureSize);
        IsDebugMode = Profile.GetInteger(fileSystem, ConfigFile, GameSection, "DebugMode"u8, 0) != 0;
        ShouldDrawFps = Profile.GetInteger(fileSystem, ConfigFile, GameSection, "DrawFPS"u8, 0) != 0;
        ShouldDrawPresentation = Profile.GetInteger(fileSystem, ConfigFile, GameSection, "DrawPresentation"u8, 1) != 0;
        Graphics.SetVSync(Profile.GetInteger(fileSystem, ConfigFile, GraphSection, "VSync"u8, DefaultVSync) != 0);
    }

    /// <summary>
    /// Closes the session's log and keeps it as the last run's log.
    /// </summary>
    public void Dispose() => Error.Dispose();

    internal uint GetWallTime() => (uint)(Time.GetElapsedTime(0).Ticks / TimeSpan.TicksPerMillisecond);

    internal void EnterMap(ReadOnlyMemory<byte> name)
    {
        PreviousMapName = MapName;
        MapName = name;
    }

    private static void AddCap(List<byte> caps, bool isSet, ReadOnlySpan<byte> name)
    {
        if (isSet)
        {
            caps.AddRange(name);
        }
    }

    private static void FillColorTable(DeviceBuffers textures)
    {
        var highColor = textures.HighFormat == VidPixelFormat.R5G6B5 ? HighColorFormat.Rgb565 : HighColorFormat.Rgb555;
        int index = 0;
        for (int top = Graphics.ColorTableGroup; top <= byte.MaxValue + 1; top += Graphics.ColorTableGroup)
        {
            for (int i = 0; i < Graphics.ColorTableGroup; i++)
            {
                int red = Math.Clamp(top + Graphics.ColorTableOffsets[i * 3], 0, byte.MaxValue);
                int green = Math.Clamp(top + Graphics.ColorTableOffsets[i * 3 + 1], 0, byte.MaxValue);
                int blue = Math.Clamp(top + Graphics.ColorTableOffsets[i * 3 + 2], 0, byte.MaxValue);
                textures.ColorTable[index++] = highColor.Pack((uint)(red << 16 | green << 8 | blue));
            }
        }
    }

    [EngineRoutine(0x0042f4b0)]
    private int SetUpWindow(Size desktop, Rectangle windowRect, Rectangle clientRect)
    {
        ApplyDisplaySettings(desktop);
        int result = Graphics.SetUpDevice(ScreenSize);
        if (result != 0)
            return result;

        if (!Graphics.Flags.HasFlag(GraphicsFlags.FullScreen) || Graphics.Flags.HasFlag(GraphicsFlags.WindowViewport))
        {
            Graphics.SetViewport(RectangleF.FromLTRB(clientRect.Left - windowRect.Left, clientRect.Top - windowRect.Top, clientRect.Right - windowRect.Left, clientRect.Bottom - windowRect.Top));
        }
        else
        {
            Graphics.SetViewport(RectangleF.FromLTRB(0, 0, ScreenSize.Width, ScreenSize.Height));
        }

        Error.Write($"SetViewPort ({Graphics.Viewport.Left:F0},{Graphics.Viewport.Top:F0}) - ({Graphics.Viewport.Right:F0},{Graphics.Viewport.Bottom:F0})");
        Error.Write(Graphics.DescribeTextureStage());
        SetDefaultRenderStates();
        Error.Write(Graphics.DescribeTextureStage());
        Error.Write(Texture.CapabilitiesText);
        var textures = Graphics.Buffers;
        if (!CreateBuffers(textures))
            return 1;

        if (Graphics.Screen is not null)
        {
            Graphics.Clear(Graphics.Screen, 0xff000000);
        }

        Graphics.CreateDeviceFont(SetupFont, SetupFontSize, SpriteCatalog.DefaultMaxTextureSize);
        FillColorTable(textures);
        LogCaps();
        int bytesPerPixel = Graphics.Flags.HasFlag(GraphicsFlags.Color32) ? sizeof(uint) : sizeof(ushort);
        int lockPitch = (int)ScreenSize.Width * bytesPerPixel;
        int pitch = lockPitch / bytesPerPixel;
        Error.Write($"Pitch={bytesPerPixel * pitch} zPitch={Graphics.DepthWidth * sizeof(ushort)}");
        Graphics.ReloadPalettes();
        return 0;
    }

    private void ApplyDisplaySettings(Size desktop)
    {
        _registry.SetNumber(ScreenWidthValue, (int)ScreenSize.Width);
        _registry.SetNumber(ScreenHeightValue, (int)ScreenSize.Height);
        _registry.SetNumber(BitsPerPixelValue, ((Graphics.Flags.HasFlag(GraphicsFlags.Color32) ? 2 : 0) + 2) << 3);
        _registry.SetNumber(DeviceValue, 0);
        _registry.SetNumber(FullScreenValue, Graphics.Flags.HasFlag(GraphicsFlags.FullScreen) ? 1 : 0);
        Graphics.Flags = (_registry.GetInt(LowDetailValue, 0) & 1) != 0 ? Graphics.Flags | GraphicsFlags.LowDetail : Graphics.Flags & ~GraphicsFlags.LowDetail;
        Graphics.Flags &= ~GraphicsFlags.Software;
        Graphics.Flags = Graphics.IsNonGdiDevice ? Graphics.Flags | GraphicsFlags.NonGdi : Graphics.Flags & ~GraphicsFlags.NonGdi;
        if (Graphics.IsNonGdiDevice)
        {
            Error.Write(NonGdiLog);
        }

        Graphics.Flags &= ~GraphicsFlags.NonGdi;
        bool isTripleBuffer = _registry.GetInt(TripleBufferValue, DefaultTripleBuffer) != 0 && Graphics.Flags.HasFlag(GraphicsFlags.FullScreen) && !Graphics.Flags.HasFlag(GraphicsFlags.WindowViewport);
        Graphics.Flags = isTripleBuffer ? Graphics.Flags | GraphicsFlags.TripleBuffer : Graphics.Flags & ~GraphicsFlags.TripleBuffer;
        if (!Graphics.CanRenderWindowed)
        {
            Graphics.Flags |= GraphicsFlags.FullScreen;
        }

        var screenSize = ScreenSize;
        if (!Graphics.Flags.HasFlag(GraphicsFlags.FullScreen) && desktop.Width < screenSize.Width)
        {
            screenSize = screenSize with { Width = desktop.Width };
        }

        if (!Graphics.Flags.HasFlag(GraphicsFlags.FullScreen) && desktop.Height < screenSize.Height)
        {
            screenSize = screenSize with { Height = desktop.Height };
        }

        ScreenSize = screenSize;
        if (!Graphics.Flags.HasFlag(GraphicsFlags.FullScreen) && Graphics.Flags.HasFlag(GraphicsFlags.Color32) != (Graphics.GetDisplayBits(Graphics.DesktopFormat) == 32))
        {
            Graphics.Flags = Graphics.GetDisplayBits(Graphics.DesktopFormat) == 32 ? Graphics.Flags | GraphicsFlags.Color32 : Graphics.Flags & ~GraphicsFlags.Color32;
        }

        if ((float)(Graphics.VideoMemory / 2) <= (ScreenSize.Width * ScreenSize.Height + ScreenSize.Width * ScreenSize.Height) * 3.0f)
        {
            Graphics.Flags &= ~GraphicsFlags.TripleBuffer;
        }

        Graphics.Flags &= ~GraphicsFlags.TripleBuffer;
    }

    private void SetDefaultRenderStates()
    {
        Graphics.SetTextureStageState(0, TextureStageState.ColorArgument1, (int)TextureArgument.Texture);
        Graphics.SetTextureStageState(0, TextureStageState.ColorArgument2, (int)TextureArgument.Diffuse);
        Graphics.SetTextureStageState(0, TextureStageState.ColorOperation, (int)TextureOperation.Modulate);
        Graphics.SetTextureStageState(0, TextureStageState.AlphaArgument1, (int)TextureArgument.Texture);
        Graphics.SetTextureStageState(0, TextureStageState.AlphaArgument2, (int)TextureArgument.Diffuse);
        Graphics.SetTextureStageState(0, TextureStageState.AlphaOperation, (int)TextureOperation.Modulate);
        Graphics.SetTextureStageState(0, TextureStageState.MinFilter, 1);
        Graphics.SetTextureStageState(0, TextureStageState.MagFilter, 1);
        Graphics.Flags &= ~GraphicsFlags.SpecularEnabled;
        Graphics.Flags &= ~GraphicsFlags.AlphaBlendEnabled;
        Graphics.SetRenderState(RenderState.DitherEnable, 1);
        Graphics.SetRenderState(RenderState.LocalViewer, 0);
        Graphics.SetRenderState(RenderState.Lighting, 0);
        Graphics.SetRenderState(RenderState.AlphaTestEnable, 0);
        Graphics.SetRenderState(RenderState.DepthEnable, 1);
        Graphics.SetRenderState(RenderState.DepthFunction, 7);
    }

    private bool CreateBuffers(DeviceBuffers textures)
    {
        textures.IsCreated = true;
        textures.Light = Graphics.CreateTexture(Error, VidPixelFormat.P8, out var format);
        textures.LightFormat = format;
        if (!HasBuffer(textures.Light, LightBufferName))
            return false;

        textures.High = Graphics.CreateTexture(Error, VidPixelFormat.R5G6B5, out format);
        textures.HighFormat = format;
        if (!HasBuffer(textures.High, HighBufferName))
            return false;

        textures.Alpha = Graphics.CreateTexture(Error, VidPixelFormat.A4R4G4B4, out _);
        return HasBuffer(textures.Alpha, AlphaBufferName);
    }

    private bool HasBuffer(ReadOnlySpan<ushort> buffer, ReadOnlySpan<byte> name)
    {
        if (buffer.Length != 0)
            return true;

        Error.Error(GraphicsTag, LogError.Create, 0, name);
        return false;
    }

    private void LogCaps()
    {
        var caps = new List<byte>();
        AddCap(caps, Graphics.Flags.HasFlag(GraphicsFlags.AlphaPalette), "ALPHAPALETTE "u8);
        AddCap(caps, Graphics.Flags.HasFlag(GraphicsFlags.NonGdi), "NONGDI "u8);
        caps.AddRange(Graphics.Flags.HasFlag(GraphicsFlags.Software) ? "SOFTWARE "u8 : "HARDWARE "u8);
        AddCap(caps, Graphics.Flags.HasFlag(GraphicsFlags.LowDetail), "LOWDETAIL "u8);
        AddCap(caps, Graphics.Flags.HasFlag(GraphicsFlags.Agp), "AGP "u8);
        AddCap(caps, !Graphics.Flags.HasFlag(GraphicsFlags.FullScreen), "WINDOWED "u8);
        caps.AddRange(Graphics.Flags.HasFlag(GraphicsFlags.TripleBuffer) ? "TRIPLEBUFFER "u8 : "DOUBLEBUFFER "u8);
        AddCap(caps, Graphics.Flags.HasFlag(GraphicsFlags.DotProduct3), "DOTPRODUCT3 "u8);
        AddCap(caps, !Graphics.Flags.HasFlag(GraphicsFlags.ModulateX2), "NOTMODULATE2X "u8);
        AddCap(caps, !Graphics.Flags.HasFlag(GraphicsFlags.CanZBlt), "CAN'T_Z_BLT "u8);
        AddCap(caps, Graphics.Flags.HasFlag(GraphicsFlags.Color32), "COLOR32 "u8);
        AddCap(caps, Graphics.Flags.HasFlag(GraphicsFlags.VSync), "VSYNC "u8);
        caps.AddRange(Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"PIXELSHADER={Graphics.PixelShaderVersion}")));
        Error.Write($"caps={MyError.Decode(CollectionsMarshal.AsSpan(caps))}");
    }
}
