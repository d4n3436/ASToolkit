namespace ASToolkit.Core.Scripting;

/// <summary>
/// Specifies the code of an entry of a sprite action stack.
/// </summary>
public enum ActionCode
{
    /// <summary>
    /// Move to a point.
    /// </summary>
    Move = 33,

    /// <summary>
    /// Build a unit.
    /// </summary>
    BuildUnit = 35,

    /// <summary>
    /// Wait for a time.
    /// </summary>
    Pause = 40,

    /// <summary>
    /// Wait for the player to come within a radius of a point.
    /// </summary>
    FlagmanTrigger = 43,

    /// <summary>
    /// Turn the sprite into another object.
    /// </summary>
    ChangeVid = 62,

    /// <summary>
    /// Move the sprite to a point at once.
    /// </summary>
    ChangeCoor = 63,

    /// <summary>
    /// Jump to another entry of the stack.
    /// </summary>
    GotoStack = 71,

    /// <summary>
    /// Clear a stack.
    /// </summary>
    ClearStack = 72,

    /// <summary>
    /// Stop the stack.
    /// </summary>
    StopStack = 73,

    /// <summary>
    /// Wait for a quest to be active.
    /// </summary>
    WhileNotQuest = 77,

    /// <summary>
    /// Start a quest.
    /// </summary>
    SetQuest = 78,

    /// <summary>
    /// Set the goal point.
    /// </summary>
    SetGoalCoor = 91,

    /// <summary>
    /// Change the army.
    /// </summary>
    SetArmy = 97,

    /// <summary>
    /// Hide or show the sprite.
    /// </summary>
    SetInvisible = 98,

    /// <summary>
    /// Show a dialog of the sprite.
    /// </summary>
    SetDialog = 115,

    /// <summary>
    /// Show a dialog of the player.
    /// </summary>
    SetFlagmanDialog = 116,

    /// <summary>
    /// Set a script variable.
    /// </summary>
    ScriptVar = 131,

    /// <summary>
    /// Destroy an object.
    /// </summary>
    DestroyUnit = 134,

    /// <summary>
    /// Play a sound effect.
    /// </summary>
    PlaySfx = 135,

    /// <summary>
    /// Call a script function.
    /// </summary>
    ScriptEvent = 136
}
