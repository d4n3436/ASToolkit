namespace ASToolkit.Core;

/// <summary>
/// Annotates a method or constructor with an address its implementation is ported or transcribed from (Alien Shooter 1.2 GOG executable).
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
