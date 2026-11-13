namespace ASToolkit.Core.Engine;

/// <summary>
/// Annotates a method or constructor with an address its implementation is ported or transcribed from (Alien Shooter 1.2 GOG executable).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor, AllowMultiple = true)]
internal sealed class EngineRoutineAttribute : Attribute
{
    public EngineRoutineAttribute(uint address)
    {
        Address = address;
    }

    public uint Address { get; }
}
