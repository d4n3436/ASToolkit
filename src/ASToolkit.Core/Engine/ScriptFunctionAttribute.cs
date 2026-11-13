namespace ASToolkit.Core.Engine;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class ScriptFunctionAttribute : Attribute
{
    public ScriptFunctionAttribute(string name)
    {
        Name = name;
    }

    public string Name { get; }
}
