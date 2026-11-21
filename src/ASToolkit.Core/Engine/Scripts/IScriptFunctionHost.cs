namespace ASToolkit.Core.Engine.Scripts;

/// <summary>
/// Defines a host that runs the built-in functions scripts call.
/// </summary>
public interface IScriptFunctionHost
{
    /// <summary>
    /// Runs a built-in function that takes its arguments from the script's stack and pushes its result.
    /// </summary>
    /// <param name="script">The script that calls the function.</param>
    /// <param name="function">The ID of the built-in function.</param>
    void ExecFunc(Script script, int function);
}
