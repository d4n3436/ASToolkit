namespace ASToolkit.Engine;

[Flags]
internal enum TextureArgument
{
    Diffuse = 0,
    Current = 1,
    Texture = 2,
    Factor = 3,
    Specular = 4,
    SelectorMask = Complement - 1,
    Complement = 1 << 4,
    AlphaReplicate = 1 << 5
}
