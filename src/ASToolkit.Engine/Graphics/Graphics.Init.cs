using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public sealed partial class Graphics
{
    private const string ScreenWidthValue = "ScreenX";
    private const string ScreenHeightValue = "ScreenY";
    private const string FullScreenValue = "FullScreen";
    private const string SetupFont = "Courier";
    private const string LowDetailValue = "LowDetail";
    private const string TripleBufferValue = "TripleBuffer";
    private const string BitsPerPixelValue = "BPP";
    private const string DeviceValue = "Device";
    private const int DefaultTripleBuffer = 1;

    private static readonly Size SetupFontSize = new(7, 8);

    [EngineRoutine(0x0042f4b0)]
    internal int Init(Size desktop, Rectangle windowRect, Rectangle clientRect)
    {
        _session.Registry.SetNumber(ScreenWidthValue, (int)ScreenSize.Width);
        _session.Registry.SetNumber(ScreenHeightValue, (int)ScreenSize.Height);
        _session.Registry.SetNumber(BitsPerPixelValue, ((Flags.HasFlag(GraphicsFlags.Color32) ? 2 : 0) + 2) << 3);
        _session.Registry.SetNumber(DeviceValue, 0);
        _session.Registry.SetNumber(FullScreenValue, Flags.HasFlag(GraphicsFlags.FullScreen) ? 1 : 0);
        Flags = (_session.Registry.GetInt(LowDetailValue, 0) & 1) != 0 ? Flags | GraphicsFlags.LowDetail : Flags & ~GraphicsFlags.LowDetail;
        Flags &= ~GraphicsFlags.Software;
        Flags = IsNonGdiDevice ? Flags | GraphicsFlags.NonGdi : Flags & ~GraphicsFlags.NonGdi;
        if (IsNonGdiDevice)
        {
            Error.Log("zm_nongdi"u8);
        }

        Flags &= ~GraphicsFlags.NonGdi;
        bool isTripleBuffer = _session.Registry.GetInt(TripleBufferValue, DefaultTripleBuffer) != 0 && Flags.HasFlag(GraphicsFlags.FullScreen) && !Flags.HasFlag(GraphicsFlags.WindowViewport);
        Flags = isTripleBuffer ? Flags | GraphicsFlags.TripleBuffer : Flags & ~GraphicsFlags.TripleBuffer;
        if (!CanRenderWindowed)
        {
            Flags |= GraphicsFlags.FullScreen;
        }

        var screenSize = ScreenSize;
        if (!Flags.HasFlag(GraphicsFlags.FullScreen) && desktop.Width < screenSize.Width)
        {
            screenSize = screenSize with { Width = desktop.Width };
        }

        if (!Flags.HasFlag(GraphicsFlags.FullScreen) && desktop.Height < screenSize.Height)
        {
            screenSize = screenSize with { Height = desktop.Height };
        }

        ScreenSize = screenSize;
        if (!Flags.HasFlag(GraphicsFlags.FullScreen) && Flags.HasFlag(GraphicsFlags.Color32) != (GetDisplayBits(DesktopFormat) == 32))
        {
            Flags = GetDisplayBits(DesktopFormat) == 32 ? Flags | GraphicsFlags.Color32 : Flags & ~GraphicsFlags.Color32;
        }

        const int halfVideoMemory = VideoMemory / 2;
        if (halfVideoMemory <= (ScreenSize.Width * ScreenSize.Height + ScreenSize.Width * ScreenSize.Height) * 3.0f)
        {
            Flags &= ~GraphicsFlags.TripleBuffer;
        }

        Flags &= ~GraphicsFlags.TripleBuffer;
        int result = SetUpDevice(ScreenSize);
        if (result != 0)
            return result;

        if (!Flags.HasFlag(GraphicsFlags.FullScreen) || Flags.HasFlag(GraphicsFlags.WindowViewport))
        {
            SetViewport(RectangleF.FromLTRB(clientRect.Left - windowRect.Left, clientRect.Top - windowRect.Top, clientRect.Right - windowRect.Left, clientRect.Bottom - windowRect.Top));
        }
        else
        {
            SetViewport(RectangleF.FromLTRB(0, 0, ScreenSize.Width, ScreenSize.Height));
        }

        Error.Log($"SetViewPort ({Viewport.Left:F0},{Viewport.Top:F0}) - ({Viewport.Right:F0},{Viewport.Bottom:F0})");
        Error.Log(DescribeTextureStage());
        SetDefaultState();
        Error.Log(DescribeTextureStage());
        Error.Log(Texture.SetDeviceCapabilities(_session));
        Buffers.IsCreated = true;
        Buffers.Light = CreateBufferTexture(VidPixelFormat.P8);
        if (Buffers.Light.IsEmpty)
        {
            Error.Error("GRAPH"u8, LogError.Create, 0, "light buffer"u8);
            return 1;
        }

        Buffers.High = CreateBufferTexture(VidPixelFormat.R5G6B5);
        if (Buffers.High.IsEmpty)
        {
            Error.Error("GRAPH"u8, LogError.Create, 0, "hiBuffer"u8);
            return 1;
        }

        Buffers.Alpha = CreateBufferTexture(VidPixelFormat.A4R4G4B4);
        if (Buffers.Alpha.IsEmpty)
        {
            Error.Error("GRAPH"u8, LogError.Create, 0, "alphaBuffer"u8);
            return 1;
        }

        if (Screen is not null)
        {
            ClearScreen(Screen, 0xff000000);
        }

        CreateDeviceFont(SetupFont, SetupFontSize, Map.DefaultMaxTextureSize);
        var highColor = Buffers.High!.Format == VidPixelFormat.R5G6B5 ? HighColorFormat.Rgb565 : HighColorFormat.Rgb555;
        int index = 0;
        for (int top = ColorTableGroup; top <= byte.MaxValue + 1; top += ColorTableGroup)
        {
            for (int i = 0; i < ColorTableGroup; i++)
            {
                Buffers.ColorTable[index++] = highColor.Pack(PackColor(top + ColorTableOffsets[i * 3], top + ColorTableOffsets[i * 3 + 1], top + ColorTableOffsets[i * 3 + 2]));
            }
        }

        var caps = new List<byte>();
        if (Flags.HasFlag(GraphicsFlags.AlphaPalette))
        {
            caps.AddRange("ALPHAPALETTE "u8);
        }

        if (Flags.HasFlag(GraphicsFlags.NonGdi))
        {
            caps.AddRange("NONGDI "u8);
        }

        caps.AddRange(Flags.HasFlag(GraphicsFlags.Software) ? "SOFTWARE "u8 : "HARDWARE "u8);
        if (Flags.HasFlag(GraphicsFlags.LowDetail))
        {
            caps.AddRange("LOWDETAIL "u8);
        }

        if (Flags.HasFlag(GraphicsFlags.Agp))
        {
            caps.AddRange("AGP "u8);
        }

        if (!Flags.HasFlag(GraphicsFlags.FullScreen))
        {
            caps.AddRange("WINDOWED "u8);
        }

        caps.AddRange(Flags.HasFlag(GraphicsFlags.TripleBuffer) ? "TRIPLEBUFFER "u8 : "DOUBLEBUFFER "u8);
        if (Flags.HasFlag(GraphicsFlags.DotProduct3))
        {
            caps.AddRange("DOTPRODUCT3 "u8);
        }

        if (!Flags.HasFlag(GraphicsFlags.ModulateX2))
        {
            caps.AddRange("NOTMODULATE2X "u8);
        }

        if (!Flags.HasFlag(GraphicsFlags.CanZBlt))
        {
            caps.AddRange("CAN'T_Z_BLT "u8);
        }

        if (Flags.HasFlag(GraphicsFlags.Color32))
        {
            caps.AddRange("COLOR32 "u8);
        }

        if (Flags.HasFlag(GraphicsFlags.VSync))
        {
            caps.AddRange("VSYNC "u8);
        }

        caps.AddRange(Encoding.ASCII.GetBytes($"PIXELSHADER={PixelShaderVersion}"));
        Error.Log($"caps={MyError.Decode(CollectionsMarshal.AsSpan(caps))}");
        int bytesPerPixel = Flags.HasFlag(GraphicsFlags.Color32) ? sizeof(uint) : sizeof(ushort);
        int lockPitch = (int)ScreenSize.Width * bytesPerPixel;
        int pitch = lockPitch / bytesPerPixel;
        Error.Log($"Pitch={bytesPerPixel * pitch} zPitch={DepthWidth * sizeof(ushort)}");
        ReloadPalettes();
        return 0;
    }

    private void SetDefaultState()
    {
        SetTextureStageState(0, TextureStageState.ColorArgument1, (int)TextureArgument.Texture);
        SetTextureStageState(0, TextureStageState.ColorArgument2, (int)TextureArgument.Diffuse);
        SetTextureStageState(0, TextureStageState.ColorOperation, (int)TextureOperation.Modulate);
        SetTextureStageState(0, TextureStageState.AlphaArgument1, (int)TextureArgument.Texture);
        SetTextureStageState(0, TextureStageState.AlphaArgument2, (int)TextureArgument.Diffuse);
        SetTextureStageState(0, TextureStageState.AlphaOperation, (int)TextureOperation.Modulate);
        SetTextureStageState(0, TextureStageState.MinFilter, 1);
        SetTextureStageState(0, TextureStageState.MagFilter, 1);
        Flags &= ~GraphicsFlags.SpecularEnabled;
        Flags &= ~GraphicsFlags.AlphaBlendEnabled;
        SetRenderState(RenderState.DitherEnable, 1);
        SetRenderState(RenderState.LocalViewer, 0);
        SetRenderState(RenderState.Lighting, 0);
        SetRenderState(RenderState.AlphaTestEnable, 0);
        SetRenderState(RenderState.DepthEnable, 1);
        SetRenderState(RenderState.DepthFunction, 7);
    }
}
