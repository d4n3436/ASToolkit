namespace ASToolkit.Engine.IO;

internal ref struct PrintOutput
{
    private readonly Span<byte> _destination;

    public PrintOutput(Span<byte> destination)
    {
        _destination = destination;
    }

    public int Length { get; private set; }

    public void Put(byte value)
    {
        if (Length < _destination.Length)
        {
            _destination[Length++] = value;
        }
    }

    public void Put(scoped ReadOnlySpan<byte> values)
    {
        foreach (byte value in values)
        {
            Put(value);
        }
    }
}
