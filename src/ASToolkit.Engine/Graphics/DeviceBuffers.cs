namespace ASToolkit.Engine;

internal sealed class DeviceBuffers
{
    public Texture? Light { get; set; }

    public Texture? High { get; set; }

    public Texture? Alpha { get; set; }

    public ushort[] ColorTable { get; set; } = new ushort[Graphics.ColorTableLength];

    public bool IsCreated { get; set; }
}
