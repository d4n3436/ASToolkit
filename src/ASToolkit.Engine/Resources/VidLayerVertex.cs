using System.Runtime.InteropServices;

namespace ASToolkit.Engine;

[StructLayout(LayoutKind.Explicit, Size = 10)]
internal readonly struct VidLayerVertex
{
    [FieldOffset(0)]
    public readonly short X;

    [FieldOffset(2)]
    public readonly short Y;

    [FieldOffset(4)]
    public readonly short Height;

    [FieldOffset(6)]
    public readonly short TextureX;

    [FieldOffset(8)]
    public readonly short TextureY;

    public VidLayerVertex(short x, short y, short height, short textureX, short textureY)
    {
        X = x;
        Y = y;
        Height = height;
        TextureX = textureX;
        TextureY = textureY;
    }
}
