namespace ASToolkit.Engine;

[Flags]
internal enum VidFlags
{
    None = 0,
    ColorKeyframes = 1 << 0,
    ScaleKeyframes = 1 << 1,
    OffsetKeyframes = 1 << 2,
    RotationKeyframes = 1 << 3,
    Keyframes = ColorKeyframes | ScaleKeyframes | OffsetKeyframes | RotationKeyframes,
    RegionReplaced = 1 << 4,
    BuiltTerrain = 1 << 5,
    Hidden = 1 << 6,
}
