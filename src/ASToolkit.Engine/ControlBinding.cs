namespace ASToolkit.Engine;

internal readonly record struct ControlBinding(VirtualKey Primary, VirtualKey Secondary)
{
    public bool IsMatch(VirtualKey key) => key == Primary || key == Secondary;
}
