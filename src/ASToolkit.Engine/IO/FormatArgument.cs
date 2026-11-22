namespace ASToolkit.Engine.IO;

internal readonly struct FormatArgument
{
    public FormatArgument(int number)
    {
        Number = number;
    }

    public FormatArgument(ReadOnlyMemory<byte> text)
    {
        Text = text;
        IsText = true;
    }

    public int Number { get; }

    public ReadOnlyMemory<byte> Text { get; }

    public bool IsText { get; }

    public static implicit operator FormatArgument(int number) => new(number);

    public static implicit operator FormatArgument(ReadOnlyMemory<byte> text) => new(text);
}
