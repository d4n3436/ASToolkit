using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents an entry of a sprite's order stack.
/// </summary>
/// <param name="Value">The action code, or a state for a state entry.</param>
/// <param name="Var1">The first argument.</param>
/// <param name="Var2">The second argument.</param>
/// <param name="Var3">The third argument.</param>
/// <param name="Target">The sprite the entry names.</param>
public readonly record struct Act(int Value, int Var1, int Var2, int Var3, Sprite? Target = null)
{
    /// <summary>
    /// Represents the highest act that sets a state.
    /// </summary>
    public const int LastState = 0x10;
    
    /// <summary>
    /// Represents the act of a stop marker.
    /// </summary>
    public const int StopMarker = (int)ActionCode.StopStack;
    
    /// <summary>
    /// Represents the act of an entry whose sprite was removed.
    /// </summary>
    public const int ForgottenOrder = 0xff;

    /// <summary>
    /// Gets the action of the entry.
    /// </summary>
    public ActionCode Code => (ActionCode)(Value & byte.MaxValue);

    /// <summary>
    /// Creates an entry from a map sprite's stack action.
    /// </summary>
    /// <param name="action">The stack action.</param>
    /// <returns>The entry.</returns>
    public static Act FromAction(StackAction action) => new(action.Act, action.Var1, action.Var2, action.Var3);

    /// <summary>
    /// Returns whether an action's entry targets a sprite.
    /// </summary>
    /// <param name="code">The action.</param>
    /// <returns><see langword="true"/> if the entry targets a sprite; otherwise, <see langword="false"/>.</returns>
    public static bool HasSpriteTarget(ActionCode code) =>
        code is ActionCode.Attack or ActionCode.MoveTo or ActionCode.RestoreCommand or ActionCode.CopyStackTo or
            ActionCode.LinkEngine or ActionCode.ClashEngine or ActionCode.ForceLinkEngine;
}
