namespace ASToolkit.Engine;

[Flags]
internal enum GraphicsFlags
{
    None = 0,
    MenuLoop = 1 << 0,
    Color32 = 1 << 1,
    AlphaPalette = 1 << 2,
    Agp = 1 << 3,
    NonGdi = 1 << 4,
    Software = 1 << 5,
    TripleBuffer = 1 << 6,
    FullScreen = 1 << 7,
    VSync = 1 << 8,
    LowDetail = 1 << 9,
    WindowViewport = 1 << 10,
    DotProduct3 = 1 << 11,
    ModulateX2 = 1 << 12,
    SpecularEnabled = 1 << 13,
    AlphaBlendEnabled = 1 << 14,
    CanZBlt = 1 << 15,
    PreTact = 1 << 16
}
