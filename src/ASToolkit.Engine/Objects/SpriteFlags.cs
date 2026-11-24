namespace ASToolkit.Engine;

[Flags]
internal enum SpriteFlags : uint
{
    None = 0,
    Command = ((1 << 5) - 1) << 2,
    Moving = 1 << 7,
    Removed = 1 << 8,
    PlayedStateSound = 1 << 9,
    Blocked = 1 << 10,
    Army = ((1 << 2) - 1) << 11,
    AwaitingSecondPulse = 1 << 13,
    ReachedX = 1 << 14,
    ReachedY = 1 << 15,
    Invisible = 1 << 16,
}
