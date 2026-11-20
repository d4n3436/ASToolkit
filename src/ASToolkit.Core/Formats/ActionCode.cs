namespace ASToolkit.Core.Formats;

/// <summary>
/// Specifies the code of an entry of a sprite action stack.
/// </summary>
public enum ActionCode
{
    /// <summary>
    /// Attack an object.
    /// </summary>
    Attack = 32,

    /// <summary>
    /// Move to a point.
    /// </summary>
    Move = 33,

    /// <summary>
    /// Move to an object.
    /// </summary>
    MoveTo = 34,

    /// <summary>
    /// Build a unit.
    /// </summary>
    BuildUnit = 35,

    /// <summary>
    /// Patrol between the current position and a point.
    /// </summary>
    Patrol = 36,

    /// <summary>
    /// Fire once at a point.
    /// </summary>
    CoorAttack = 37,

    /// <summary>
    /// Idle, turn the head or do nothing at random.
    /// </summary>
    Random = 38,

    /// <summary>
    /// Stop moving and drop the current command.
    /// </summary>
    Stop = 39,

    /// <summary>
    /// Wait for a time.
    /// </summary>
    Pause = 40,

    /// <summary>
    /// Turn the sprite toward a direction.
    /// </summary>
    Turn = 41,

    /// <summary>
    /// Drop the current command.
    /// </summary>
    ClearCommand = 42,

    /// <summary>
    /// Wait for the player to come within a radius of a point.
    /// </summary>
    FlagmanTrigger = 43,

    /// <summary>
    /// Select the sprite.
    /// </summary>
    Select = 44,

    /// <summary>
    /// Add an item to the carried items.
    /// </summary>
    AddItem = 54,

    /// <summary>
    /// Remove an item from the carried items.
    /// </summary>
    DeleteItem = 55,

    /// <summary>
    /// Return whether an item is carried.
    /// </summary>
    HaveItem = 56,

    /// <summary>
    /// Remove every carried item.
    /// </summary>
    DeleteAllItems = 57,

    /// <summary>
    /// Return the item in a slot.
    /// </summary>
    GetItem = 58,

    /// <summary>
    /// Return a random carried item of a sprite type.
    /// </summary>
    GetItemType = 59,

    /// <summary>
    /// Set the direction.
    /// </summary>
    ChangeDirection = 60,

    /// <summary>
    /// Set the animation state.
    /// </summary>
    ChangeAnimation = 61,

    /// <summary>
    /// Turn the sprite into another object.
    /// </summary>
    ChangeVid = 62,

    /// <summary>
    /// Move the sprite to a point at once.
    /// </summary>
    ChangeCoor = 63,

    /// <summary>
    /// Change the sprite's height.
    /// </summary>
    ChangeCoorZ = 69,

    /// <summary>
    /// Push the current command onto the action stack.
    /// </summary>
    BackupCommand = 70,

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
    /// Restore a command pushed onto the action stack.
    /// </summary>
    RestoreCommand = 74,

    /// <summary>
    /// Copy the action stack to another sprite up to the stop entry.
    /// </summary>
    CopyStackTo = 75,

    /// <summary>
    /// Return the number of entries of the action stack.
    /// </summary>
    GetStackCount = 76,

    /// <summary>
    /// Wait for a quest to be active.
    /// </summary>
    WhileNotQuest = 77,

    /// <summary>
    /// Start a quest.
    /// </summary>
    SetQuest = 78,

    /// <summary>
    /// Write the sprite to a saved game.
    /// </summary>
    Save = 80,

    /// <summary>
    /// Read the sprite from a saved game.
    /// </summary>
    Restore = 81,

    /// <summary>
    /// Take damage from a unit.
    /// </summary>
    Damage = 85,

    /// <summary>
    /// Restore the hit points, the ammunition and the linked object.
    /// </summary>
    Repair = 86,

    /// <summary>
    /// Return the hit points.
    /// </summary>
    GetHp = 87,

    /// <summary>
    /// Set the hit points.
    /// </summary>
    SetHp = 88,

    /// <summary>
    /// Return the hit points as a percentage of the maximum.
    /// </summary>
    GetPercentHp = 89,

    /// <summary>
    /// Return the goal of the current command.
    /// </summary>
    GetGoal = 90,

    /// <summary>
    /// Set the goal point.
    /// </summary>
    SetGoalCoor = 91,

    /// <summary>
    /// Return the ammunition.
    /// </summary>
    GetAmmo = 92,

    /// <summary>
    /// Add ammunition.
    /// </summary>
    AddAmmo = 93,

    /// <summary>
    /// Return the behavior mask.
    /// </summary>
    GetBehave = 94,

    /// <summary>
    /// Set the behavior mask.
    /// </summary>
    SetBehave = 95,

    /// <summary>
    /// Return the army.
    /// </summary>
    GetArmy = 96,

    /// <summary>
    /// Change the army.
    /// </summary>
    SetArmy = 97,

    /// <summary>
    /// Hide or show the sprite.
    /// </summary>
    SetInvisible = 98,

    /// <summary>
    /// Return the battle range, the linked object's when there is one.
    /// </summary>
    GetBattleRange = 100,

    /// <summary>
    /// Return the linked object.
    /// </summary>
    GetLink = 101,

    /// <summary>
    /// Link an object to the sprite.
    /// </summary>
    SetLink = 102,

    /// <summary>
    /// Return the object the sprite is linked to.
    /// </summary>
    GetUplink = 103,

    /// <summary>
    /// Unlink the sprite from its parent.
    /// </summary>
    SetUplink = 104,

    /// <summary>
    /// Return the timer.
    /// </summary>
    GetTimer = 105,

    /// <summary>
    /// Set the timer.
    /// </summary>
    SetTimer = 106,

    /// <summary>
    /// Return the vertical speed.
    /// </summary>
    GetZSpeed = 107,

    /// <summary>
    /// Set the vertical speed.
    /// </summary>
    SetZSpeed = 108,

    /// <summary>
    /// Return the speed.
    /// </summary>
    GetSpeed = 109,

    /// <summary>
    /// Set the speed.
    /// </summary>
    SetSpeed = 110,

    /// <summary>
    /// Return the current command.
    /// </summary>
    GetCommand = 111,

    /// <summary>
    /// Set the time left before the sprite dies.
    /// </summary>
    SetDeathTimer = 112,

    /// <summary>
    /// Show a dialog of the sprite.
    /// </summary>
    SetDialog = 115,

    /// <summary>
    /// Show a dialog of the player.
    /// </summary>
    SetFlagmanDialog = 116,

    /// <summary>
    /// Set the text of a text sprite.
    /// </summary>
    SetText = 120,

    /// <summary>
    /// Return where a text sprite loads its text from.
    /// </summary>
    GetTextDesc = 121,

    /// <summary>
    /// Set the number of characters a text sprite shows.
    /// </summary>
    SetTextCount = 122,

    /// <summary>
    /// Set the file a sprite loads.
    /// </summary>
    SetFile = 123,

    /// <summary>
    /// Return the text a text sprite shows.
    /// </summary>
    GetText = 124,

    /// <summary>
    /// Run the next command at the end of an animation.
    /// </summary>
    NextCommand = 130,

    /// <summary>
    /// Set a script variable.
    /// </summary>
    ScriptVar = 131,

    /// <summary>
    /// Hide the sprite for an editor undo.
    /// </summary>
    UndoRemove = 132,

    /// <summary>
    /// Show the sprite for an editor undo.
    /// </summary>
    UndoInsert = 133,

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
    ScriptEvent = 136,

    /// <summary>
    /// Couple a train car to an engine.
    /// </summary>
    LinkEngine = 150,

    /// <summary>
    /// Ram an engine.
    /// </summary>
    ClashEngine = 151,

    /// <summary>
    /// Couple uncoupled train cars.
    /// </summary>
    ForceLinkEngine = 152,

    /// <summary>
    /// Set the behavior of a train.
    /// </summary>
    TrainBehave = 153,

    /// <summary>
    /// Return the first engine of a train.
    /// </summary>
    FirstEngine = 154,

    /// <summary>
    /// Return the last engine of a train.
    /// </summary>
    LastEngine = 155,

    /// <summary>
    /// Return the next engine of a train.
    /// </summary>
    NextEngine = 156,

    /// <summary>
    /// Return whether the car leads its train.
    /// </summary>
    IsFirst = 157,

    /// <summary>
    /// Return whether an engine belongs to the train.
    /// </summary>
    InTrain = 158,

    /// <summary>
    /// Return whether the sprite is an engine.
    /// </summary>
    IsTrain = 159,

    /// <summary>
    /// Restore the sprite's state from a saved stream (older scripts).
    /// </summary>
    RestoreOld = 200,

    /// <summary>
    /// Change the facing to the second value and the animation to the first.
    /// </summary>
    ChangeAction = 201,

    /// <summary>
    /// Return the current animation.
    /// </summary>
    GetAction = 202,

    /// <summary>
    /// Move the sprite to a point at its current height.
    /// </summary>
    ChangePosition = 203,

    /// <summary>
    /// Move the sprite to a height at its current point.
    /// </summary>
    ChangeHeight = 204,

    /// <summary>
    /// Do nothing.
    /// </summary>
    None = 255
}
