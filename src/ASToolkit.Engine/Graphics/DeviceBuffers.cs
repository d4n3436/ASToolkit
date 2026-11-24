using ASToolkit.Formats;

namespace ASToolkit.Engine;

internal sealed class DeviceBuffers
{
    public ushort[] Light { get; set; } = [];

    public VidPixelFormat LightFormat { get; set; }

    public ushort[] High { get; set; } = [];

    public VidPixelFormat HighFormat { get; set; }

    public ushort[] Alpha { get; set; } = [];

    public ushort[] ColorTable { get; set; } = new ushort[Screen.ColorTableLength];

    public bool IsCreated { get; set; }
}
