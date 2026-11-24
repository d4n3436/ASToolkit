using ASToolkit.Formats;

namespace ASToolkit.Engine;

public partial class Map
{
    internal const int ControlledPlayer = 0;


    /// <summary>
    /// Sends an action to a sprite.
    /// </summary>
    /// <param name="sprite">The sprite.</param>
    /// <param name="code">The action.</param>
    /// <param name="var1">The first argument.</param>
    /// <param name="var2">The second argument.</param>
    /// <param name="var3">The third argument.</param>
    /// <param name="target">The sprite argument.</param>
    /// <returns>The action's result.</returns>
    public static int Action(Sprite sprite, ActionCode code, int var1 = 0, int var2 = 0, int var3 = 0, Sprite? target = null) =>
        sprite.Action(code, var1, var2, var3, target);

}
