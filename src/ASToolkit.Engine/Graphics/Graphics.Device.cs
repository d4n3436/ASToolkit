using System.Drawing;
using System.Numerics;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public sealed partial class Graphics
{
    internal const int ColorTableLength = 256;
    internal const int TextureSide = 256;
    internal const int ColorTableGroup = 8;
    internal const int VideoMemory = 8000000;
    internal const VidPixelFormat DesktopFormat = VidPixelFormat.X8R8G8B8;
    internal const bool IsPowerOfTwoRequired = false;
    internal const bool AreSquareTexturesRequired = false;
    internal const bool IsAlphaPaletteSupported = true;
    internal const bool IsConditionalNonPowerOfTwoSupported = false;
    internal const bool IsModulate2XSupported = true;
    internal const bool IsDotProduct3Supported = true;
    internal const int DevicePixelShaderVersion = unchecked((int)0xffff0104);

    internal static readonly bool CanRenderWindowed = true;
    internal static readonly bool IsNonGdiDevice = false;
    internal static readonly bool CanTextureFromNonLocalVideoMemory = false;
    private const int GrayLength = 256;
    private const int DeviceFailure = 1;

    private static readonly ushort[] DefaultTable = CreateColorTable(HighColorFormat.Rgb565);

    private static readonly TextureStage DefaultStage = new(TextureOperation.Modulate, TextureArgument.Texture, TextureArgument.Current, TextureOperation.SelectFirst, TextureArgument.Texture, TextureArgument.Current);

    private static readonly DisplayMode[] DeviceDisplayModes =
    [
        new(new Size(320, 200), VidPixelFormat.X8R8G8B8),
        new(new Size(320, 240), VidPixelFormat.X8R8G8B8),
        new(new Size(640, 350), VidPixelFormat.X8R8G8B8),
        new(new Size(640, 400), VidPixelFormat.X8R8G8B8),
        new(new Size(640, 480), VidPixelFormat.X8R8G8B8),
        new(new Size(720, 400), VidPixelFormat.X8R8G8B8),
        new(new Size(720, 480), VidPixelFormat.X8R8G8B8),
        new(new Size(768, 480), VidPixelFormat.X8R8G8B8),
        new(new Size(800, 500), VidPixelFormat.X8R8G8B8),
        new(new Size(800, 600), VidPixelFormat.X8R8G8B8),
        new(new Size(864, 486), VidPixelFormat.X8R8G8B8),
        new(new Size(928, 580), VidPixelFormat.X8R8G8B8),
        new(new Size(960, 600), VidPixelFormat.X8R8G8B8),
        new(new Size(1024, 576), VidPixelFormat.X8R8G8B8),
        new(new Size(1024, 768), VidPixelFormat.X8R8G8B8),
        new(new Size(320, 200), VidPixelFormat.R5G6B5),
        new(new Size(320, 240), VidPixelFormat.R5G6B5),
        new(new Size(640, 350), VidPixelFormat.R5G6B5),
        new(new Size(640, 400), VidPixelFormat.R5G6B5),
        new(new Size(640, 480), VidPixelFormat.R5G6B5),
        new(new Size(720, 400), VidPixelFormat.R5G6B5),
        new(new Size(720, 480), VidPixelFormat.R5G6B5),
        new(new Size(768, 480), VidPixelFormat.R5G6B5),
        new(new Size(800, 500), VidPixelFormat.R5G6B5),
        new(new Size(800, 600), VidPixelFormat.R5G6B5),
        new(new Size(864, 486), VidPixelFormat.R5G6B5),
        new(new Size(928, 580), VidPixelFormat.R5G6B5),
        new(new Size(960, 600), VidPixelFormat.R5G6B5),
        new(new Size(1024, 576), VidPixelFormat.R5G6B5),
        new(new Size(1024, 768), VidPixelFormat.R5G6B5)
    ];

    private readonly DdDriver _driver = new(DeviceDisplayModes);

    internal static Size DeviceMaxTextureSize => new(0x8000, 0x8000);

    internal static ReadOnlySpan<VidPixelFormat> DeviceTextureFormats =>
    [
        VidPixelFormat.A8R8G8B8, VidPixelFormat.X8R8G8B8, VidPixelFormat.R5G6B5, VidPixelFormat.X1R5G5B5, VidPixelFormat.A1R5G5B5, VidPixelFormat.A4R4G4B4,
        VidPixelFormat.A8, VidPixelFormat.L8, VidPixelFormat.A8L8, VidPixelFormat.V8U8, VidPixelFormat.L6V5U5, VidPixelFormat.X8L8V8U8, VidPixelFormat.Q8W8V8U8,
        VidPixelFormat.V16U16, VidPixelFormat.Dxt1, VidPixelFormat.Dxt2, VidPixelFormat.Dxt3, VidPixelFormat.Dxt4, VidPixelFormat.Dxt5
    ];

    internal static ReadOnlySpan<ushort> DefaultColorTable => DefaultTable;

    internal static ReadOnlySpan<int> ColorTableOffsets =>
    [
        -8, -8, -8,
        -7, -7, 0,
        0, -6, -6,
        0, -5, 0,
        -4, 0, -4,
        -3, 0, 0,
        0, 0, -2,
        0, 0, 0
    ];

    internal int PixelShaderVersion { get; set; }

    internal VidPixelFormat DisplayFormat { get; private set; }

    internal int DepthWidth { get; private set; }

    internal DeviceBuffers Buffers { get; } = new();

    internal Texture LightTexture => EnsureBuffers().Light!;

    internal Texture HighTexture => EnsureBuffers().High!;

    internal Texture AlphaTexture => EnsureBuffers().Alpha!;

    internal Texture LightPhaseTexture => LightPhase == 0 ? LightTexture : HighTexture;

    internal int LightPhase { get; set; }

    internal TextureStage DeviceStage { get; private set; } = DefaultStage;

    internal Matrix4x4 WorldTransform { get; set; } = Matrix4x4.Identity;

    internal Matrix4x4 ViewTransform { get; set; } = Matrix4x4.Identity;

    internal uint TextureFactor { get; private set; }

    internal bool IsDoubled => DeviceStage.ColorOperation == TextureOperation.Modulate2X;

    internal bool IsAlphaModulated => DeviceStage.AlphaOperation == TextureOperation.Modulate;

    private static uint[] GrayPalette { get; } = CreateGrayPalette();

    internal static int GetDisplayBits(VidPixelFormat format) => format switch
    {
        VidPixelFormat.P8 => 8,
        VidPixelFormat.R8G8B8 => 24,
        VidPixelFormat.A8R8G8B8 or VidPixelFormat.X8R8G8B8 => 32,
        VidPixelFormat.R5G6B5 or VidPixelFormat.X1R5G5B5 or VidPixelFormat.A1R5G5B5 or VidPixelFormat.A4R4G4B4 => 16,
        VidPixelFormat.Dxt1 => 4,
        VidPixelFormat.Dxt3 or VidPixelFormat.Dxt5 => 8,
        _ => 0
    };

    internal void SetUpSettings(bool hasAppMenu, bool isVSync, bool isColor32, bool isFullScreen)
    {
        Flags &= ~(GraphicsFlags.MenuLoop | GraphicsFlags.VSync | GraphicsFlags.WindowViewport | GraphicsFlags.PreTact | GraphicsFlags.Color32 | GraphicsFlags.FullScreen);
        if (hasAppMenu)
        {
            Flags |= GraphicsFlags.WindowViewport;
        }

        if (isVSync)
        {
            Flags |= GraphicsFlags.VSync;
        }

        if (isColor32)
        {
            Flags |= GraphicsFlags.Color32;
        }

        if (isFullScreen)
        {
            Flags |= GraphicsFlags.FullScreen;
        }
    }

    internal void SetTextureStageState(int stage, TextureStageState state, int value)
    {
        if (stage != 0)
            return;

        var current = DeviceStage;
        DeviceStage = state switch
        {
            TextureStageState.ColorOperation => current with { ColorOperation = (TextureOperation)value },
            TextureStageState.ColorArgument1 => current with { ColorArgument1 = (TextureArgument)value },
            TextureStageState.ColorArgument2 => current with { ColorArgument2 = (TextureArgument)value },
            TextureStageState.AlphaOperation => current with { AlphaOperation = (TextureOperation)value },
            TextureStageState.AlphaArgument1 => current with { AlphaArgument1 = (TextureArgument)value },
            TextureStageState.AlphaArgument2 => current with { AlphaArgument2 = (TextureArgument)value },
            _ => current
        };
    }

    [EngineRoutine(0x00401c80)]
    internal int SetUpDevice(SizeF screenSize)
    {
        const GraphicsFlags deviceMask = GraphicsFlags.AlphaPalette | GraphicsFlags.Agp | GraphicsFlags.DotProduct3 | GraphicsFlags.ModulateX2;

        VidPixelFormat format;
        if (!Flags.HasFlag(GraphicsFlags.FullScreen))
        {
            format = DesktopFormat;
        }
        else
        {
            int bits = Flags.HasFlag(GraphicsFlags.Color32) ? 0x20 : 0x10;
            int index = _driver.GetMode(Size.Truncate(screenSize), bits);
            if (index < 0)
            {
                Error?.Log($"Selected display mode {screenSize.Width:F0}x{screenSize.Height:F0}");
                Error?.Error(GraphicsLogTag, LogError.Text, 0, "Unsupported resolution"u8);
                return DeviceFailure;
            }

            format = _driver.Modes[index].Format;
        }

        DisplayFormat = format;
        Error?.Log($"Selected display mode {screenSize.Width:F0}x{screenSize.Height:F0} {MyError.Decode(GetFormatName(format))} desktop {MyError.Decode(GetFormatName(DesktopFormat))} zbuffer {MyError.Decode(GetFormatName(VidPixelFormat.None))}");
        Flags &= ~deviceMask;
        if (IsDotProduct3Supported)
        {
            Flags |= GraphicsFlags.DotProduct3;
        }

        if (CanTextureFromNonLocalVideoMemory)
        {
            Flags |= GraphicsFlags.Agp;
        }

        if (IsAlphaPaletteSupported)
        {
            Flags |= GraphicsFlags.AlphaPalette;
        }

        if (IsModulate2XSupported)
        {
            Flags |= GraphicsFlags.ModulateX2;
        }

        PixelShaderVersion = DevicePixelShaderVersion;
        DepthWidth = (int)screenSize.Width;
        return 0;
    }

    [EngineRoutine(0x0042eb10)]
    internal byte[] DescribeTextureStage()
    {
        var stage = DeviceStage;
        List<byte> text = [];
        text.AddRange("Op="u8);
        text.AddRange(GetOperationName(stage.ColorOperation));
        text.AddRange(" Arg1="u8);
        AppendArgument(text, stage.ColorArgument1);
        text.AddRange(" Arg2="u8);
        AppendArgument(text, stage.ColorArgument2);
        text.AddRange(" AOp="u8);
        text.AddRange(GetOperationName(stage.AlphaOperation));
        text.AddRange(" Arg1="u8);
        AppendArgument(text, stage.AlphaArgument1);
        text.AddRange(" Arg2="u8);
        AppendArgument(text, stage.AlphaArgument2);
        return text.ToArray();
    }

    [EngineRoutine(0x00430650)]
    internal void ReloadPalettes()
    {
        var light = Buffers.Light;
        if (light is null || light.IsEmpty || light.Format != VidPixelFormat.P8)
            return;

        light.Destruct();
        Buffers.Light = CreateBufferTexture(VidPixelFormat.P8);
        if (Buffers.Light.IsEmpty)
        {
            Error?.Error(GraphicsLogTag, LogError.Create, 0, "Light at RelodPalette()"u8);
        }

        Error?.Log("ReloadPalettes"u8);
    }

    [EngineRoutine(0x00401070)]
    private static ReadOnlySpan<byte> GetFormatName(VidPixelFormat format) => format switch
    {
        VidPixelFormat.R8G8B8 => "R8G8B8"u8,
        VidPixelFormat.A8R8G8B8 => "A8R8G8B8"u8,
        VidPixelFormat.X8R8G8B8 => "X8R8G8B8"u8,
        VidPixelFormat.R5G6B5 => "R5G6B5"u8,
        VidPixelFormat.X1R5G5B5 => "X1R5G5B5"u8,
        VidPixelFormat.A1R5G5B5 => "A1R5G5B5"u8,
        VidPixelFormat.A4R4G4B4 => "A4R4G4B4"u8,
        VidPixelFormat.A8 => "A8"u8,
        VidPixelFormat.A8P8 => "A8P8"u8,
        VidPixelFormat.P8 => "P8"u8,
        VidPixelFormat.L8 => "L8"u8,
        VidPixelFormat.A8L8 => "A8L8"u8,
        VidPixelFormat.A4L4 => "A4L4"u8,
        VidPixelFormat.V8U8 => "V8U8"u8,
        VidPixelFormat.Dxt1 => "DXT1"u8,
        VidPixelFormat.Dxt2 => "DXT2"u8,
        VidPixelFormat.Dxt3 => "DXT3"u8,
        VidPixelFormat.Dxt4 => "DXT4"u8,
        VidPixelFormat.Dxt5 => "DXT5"u8,
        _ => "Unknown"u8
    };

    [EngineRoutine(0x0042ea30)]
    private static void AppendArgument(List<byte> text, TextureArgument argument)
    {
        if (argument.HasFlag(TextureArgument.AlphaReplicate))
        {
            text.AddRange("alp-"u8);
        }

        if (argument.HasFlag(TextureArgument.Complement))
        {
            text.AddRange("inv-"u8);
        }

        switch (argument & TextureArgument.SelectorMask)
        {
            case TextureArgument.Texture:
                text.AddRange("tex"u8);
                break;
            case TextureArgument.Diffuse:
                text.AddRange("dif"u8);
                break;
            case TextureArgument.Specular:
                text.AddRange("spec"u8);
                break;
            case TextureArgument.Current:
                text.AddRange("cur"u8);
                break;
            case TextureArgument.Factor:
                text.AddRange("tfac"u8);
                break;
        }
    }

    private static ReadOnlySpan<byte> GetOperationName(TextureOperation operation) => operation switch
    {
        TextureOperation.Disable => "dis"u8,
        TextureOperation.SelectFirst => "sel1"u8,
        TextureOperation.SelectSecond => "sel2"u8,
        TextureOperation.Modulate => "mod"u8,
        TextureOperation.BlendTextureAlpha => "tex_alpha"u8,
        _ => "unknown"u8
    };

    private static uint[] CreateGrayPalette()
    {
        uint[] palette = new uint[GrayLength];
        for (int i = 0; i < palette.Length; i++)
        {
            int level = Math.Clamp(i, 0, byte.MaxValue);
            palette[i] = 0xff000000 | (uint)(level << 16 | level << 8 | level);
        }

        return palette;
    }

    private static ushort[] CreateColorTable(HighColorFormat format)
    {
        ushort[] table = new ushort[ColorTableLength];
        int index = 0;
        for (int top = ColorTableGroup; top <= byte.MaxValue + 1; top += ColorTableGroup)
        {
            for (int i = 0; i < ColorTableGroup; i++)
            {
                table[index++] = format.Pack(PackColor(top + ColorTableOffsets[i * 3], top + ColorTableOffsets[i * 3 + 1], top + ColorTableOffsets[i * 3 + 2]));
            }
        }

        return table;
    }

    private DeviceBuffers EnsureBuffers()
    {
        if (Buffers.IsCreated)
            return Buffers;

        Buffers.IsCreated = true;
        Buffers.Light = CreateBufferTexture(VidPixelFormat.P8, "light buffer"u8);
        if (Buffers.Light.IsEmpty)
            return Buffers;

        Buffers.High = CreateBufferTexture(VidPixelFormat.R5G6B5, "hiBuffer"u8);
        if (Buffers.High.IsEmpty)
            return Buffers;

        Buffers.Alpha = CreateBufferTexture(VidPixelFormat.A4R4G4B4, "alphaBuffer"u8);
        if (Buffers.Alpha.IsEmpty)
            return Buffers;

        Buffers.ColorTable = CreateColorTable(Buffers.High.Format == VidPixelFormat.R5G6B5 ? HighColorFormat.Rgb565 : HighColorFormat.Rgb555);
        return Buffers;
    }

    private Texture CreateBufferTexture(VidPixelFormat format, ReadOnlySpan<byte> name)
    {
        var texture = CreateBufferTexture(format);
        if (texture.IsEmpty)
        {
            Error?.Error("GRAPH"u8, LogError.Create, 0, name);
        }

        return texture;
    }

    private Texture CreateBufferTexture(VidPixelFormat format)
    {
        var texture = new Texture(new Size(TextureSide, TextureSide), format, TextureFlags.None, _session);
        if (texture.Format == VidPixelFormat.P8)
        {
            texture.SetPalette(GrayPalette);
        }

        return texture;
    }
}
