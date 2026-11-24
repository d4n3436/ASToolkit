namespace ASToolkit.Engine;

[Flags]
internal enum MapFlags
{
    None = 0,
    AppMenu = 1 << 0,
    MonstersAttackNeutralTrains = 1 << 1,
    Initialized = 1 << 2,
    WindowActive = 1 << 3,
    Paused = 1 << 4,
    Loading = 1 << 5,
    LoadPending = 1 << 6,
    PlayerControlled = 1 << 7,
    RecordingDemo = 1 << 8,
    PlayingDemo = 1 << 9,
    DrawGridZ = 1 << 11,
    DrawFlagman = 1 << 12,
    DrawRails = 1 << 13,
    DrawGroups = 1 << 14,
    DrawSpriteBoxes = 1 << 15,
    DrawSoundCount = 1 << 16,
    DrawFps = 1 << 17,
    DrawPresentation = 1 << 18,
    ScriptsDisabled = 1 << 19,
    MouseTips = 1 << 20,
}
