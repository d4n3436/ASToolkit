namespace ASToolkit.Engine;

internal readonly record struct ControlBinding(int Primary, int Secondary)
{
    public bool Matches(int code) => code == Primary || code == Secondary;
}
