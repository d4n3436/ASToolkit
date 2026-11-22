namespace ASToolkit.Engine;

/// <summary>
/// Annotates a method or constructor with the address in the Alien Shooter 1.2 GOG executable that its implementation is ported or transcribed from.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property, AllowMultiple = true)]
internal sealed class EngineRoutineAttribute : Attribute
{
    public EngineRoutineAttribute(int address)
    {
        Address = address;
    }

    public int Address { get; }
}
