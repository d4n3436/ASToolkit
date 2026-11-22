namespace ASToolkit.Core.Engine;

[Flags]
internal enum TgaPacket : byte
{
    None = 0,
    CountMask = Run - 1,
    Run = 1 << 7
}
