namespace ASToolkit.Engine;

internal readonly record struct TextureStage(TextureOperation ColorOperation, TextureArgument ColorArgument1,
    TextureArgument ColorArgument2, TextureOperation AlphaOperation, TextureArgument AlphaArgument1, TextureArgument AlphaArgument2);
