using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal sealed class SpriteText
{
    public ReadOnlyMemory<byte> Shown { get; set; }

    public ReadOnlyMemory<byte> Description { get; set; }

    public int VisibleCount { get; set; }

    public TextBehavior Behavior { get; set; }

    public int LineCount { get; set; } = 1;

    public int LineWidth { get; set; }
}
