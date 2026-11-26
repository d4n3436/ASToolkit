namespace ASToolkit.Engine.Scripts;

/// <summary>
/// Specifies a built-in function of the script language by its number.
/// </summary>
public enum ScriptFunction
{
    /// <summary>
    /// Create a sprite.
    /// </summary>
    CreateSprite = 65,

    /// <summary>
    /// Return the sprite a player slot controls.
    /// </summary>
    Flagman = 66,

    /// <summary>
    /// Start iterating over the sprites of some types and armies.
    /// </summary>
    FirstUnit = 68,

    /// <summary>
    /// Continue iterating over sprites.
    /// </summary>
    NextUnit = 69,

    /// <summary>
    /// Find the sprite whose footprint holds a point.
    /// </summary>
    GetSprite = 70,

    /// <summary>
    /// Find the sprite whose box on screen holds a point.
    /// </summary>
    GetSpriteScr = 71,

    /// <summary>
    /// Find the nearest sprite within a radius.
    /// </summary>
    FindNearestSprite = 72,

    /// <summary>
    /// Start iterating over the hashed sprites in a rectangle.
    /// </summary>
    FirstInBox = 74,

    /// <summary>
    /// Continue iterating over the hashed sprites in a rectangle.
    /// </summary>
    NextInBox = 75,

    /// <summary>
    /// Start iterating over every sprite.
    /// </summary>
    FirstSprite = 76,

    /// <summary>
    /// Continue iterating over every sprite.
    /// </summary>
    NextSprite = 77,

    /// <summary>
    /// Send an action to a sprite.
    /// </summary>
    Action = 79,

    /// <summary>
    /// Return the distance from a sprite to a point.
    /// </summary>
    SizeTo = 80,

    /// <summary>
    /// Recalculate the height grid with a unit's size.
    /// </summary>
    ReCalcGridZ = 81,

    /// <summary>
    /// Add an order to a sprite's order stack.
    /// </summary>
    AddCommand = 82,

    /// <summary>
    /// Return the object ID of a sprite.
    /// </summary>
    GetUnitVid = 83,

    /// <summary>
    /// Destroy a sprite.
    /// </summary>
    Destroy = 84,

    /// <summary>
    /// Return the x coordinate of a sprite.
    /// </summary>
    GetX = 85,

    /// <summary>
    /// Return the y coordinate of a sprite.
    /// </summary>
    GetY = 86,

    /// <summary>
    /// Return the height of a sprite.
    /// </summary>
    GetZ = 87,

    /// <summary>
    /// Return the direction of a sprite.
    /// </summary>
    GetDirection = 88,

    /// <summary>
    /// Return the animation a sprite plays.
    /// </summary>
    GetAnimation = 89,

    /// <summary>
    /// Return the direction from a sprite to a point.
    /// </summary>
    DirectionTo = 90,

    /// <summary>
    /// Return a vehicle's commands as text.
    /// </summary>
    GetCommands = 96,

    /// <summary>
    /// Set a vehicle's commands from text.
    /// </summary>
    SetCommands = 97,

    /// <summary>
    /// Load a map.
    /// </summary>
    Load = 98,

    /// <summary>
    /// Save the map.
    /// </summary>
    Save = 99,

    /// <summary>
    /// Record the next loaded map into a demo file.
    /// </summary>
    SaveDemo = 100,

    /// <summary>
    /// Find a menu item by object ID and direction.
    /// </summary>
    MenuFind = 101,

    /// <summary>
    /// Load menus from a file.
    /// </summary>
    MenuLoad = 102,

    /// <summary>
    /// Remove the menus of a file, or every menu.
    /// </summary>
    MenuRelease = 103,

    /// <summary>
    /// Return the object ID of the pressed button.
    /// </summary>
    MenuNVidUnderCursor = 104,

    /// <summary>
    /// Return the direction of the pressed button.
    /// </summary>
    MenuNDirUnderCursor = 105,

    /// <summary>
    /// Send an action to the menu items of an object ID and direction.
    /// </summary>
    MenuAction = 106,

    /// <summary>
    /// Create a menu item.
    /// </summary>
    MenuCreate = 107,

    /// <summary>
    /// Return the menu item clicked with the left mouse button.
    /// </summary>
    MenuLClick = 108,

    /// <summary>
    /// Return the x coordinate of the mouse on the map.
    /// </summary>
    GetInputX = 109,

    /// <summary>
    /// Return the y coordinate of the mouse on the map.
    /// </summary>
    GetInputY = 110,

    /// <summary>
    /// Return the code of the pressed key.
    /// </summary>
    GetKey = 111,

    /// <summary>
    /// Pause or resume the game.
    /// </summary>
    Pause = 112,

    /// <summary>
    /// Set the cursor's shape.
    /// </summary>
    SetCursor = 113,

    /// <summary>
    /// Show a message on the state bar.
    /// </summary>
    MessageText = 114,

    /// <summary>
    /// Return the pressed buttons and keys.
    /// </summary>
    GetInputState = 115,

    /// <summary>
    /// Set the point the view centers on.
    /// </summary>
    SetPosition = 116,

    /// <summary>
    /// Set the view's scroll mode.
    /// </summary>
    SetScrollType = 117,

    /// <summary>
    /// Return the view's scroll mode.
    /// </summary>
    GetScrollType = 118,

    /// <summary>
    /// Return the window's width.
    /// </summary>
    ScreenX = 119,

    /// <summary>
    /// Return the window's height.
    /// </summary>
    ScreenY = 120,

    /// <summary>
    /// Allow or forbid the player to control units.
    /// </summary>
    SetPlayerControl = 121,

    /// <summary>
    /// Show or hide the state bar.
    /// </summary>
    SetStateBar = 122,

    /// <summary>
    /// Return a string of strings.ini.
    /// </summary>
    GetString = 123,

    /// <summary>
    /// Quit the game.
    /// </summary>
    Exit = 124,

    /// <summary>
    /// Convert an x coordinate to the screen.
    /// </summary>
    ToScreenX = 125,

    /// <summary>
    /// Convert a y coordinate and a height to the screen.
    /// </summary>
    ToScreenY = 126,

    /// <summary>
    /// Return the menu item clicked with the right mouse button.
    /// </summary>
    MenuRClick = 127,

    /// <summary>
    /// Bind a mouse button to the first or second action.
    /// </summary>
    SetMouseClick = 128,

    /// <summary>
    /// Move the mouse cursor on the screen.
    /// </summary>
    SetCursorPosition = 129,

    /// <summary>
    /// Set the sound volume.
    /// </summary>
    SetSoundVolume = 130,

    /// <summary>
    /// Set the music volume.
    /// </summary>
    SetMusicVolume = 131,

    /// <summary>
    /// Play a sound.
    /// </summary>
    PlaySfx = 132,

    /// <summary>
    /// Stop a sound, or every sound.
    /// </summary>
    StopSfx = 133,

    /// <summary>
    /// Stop the music.
    /// </summary>
    StopMusic = 134,

    /// <summary>
    /// Play a sound at a point of the map.
    /// </summary>
    PlaySfxFromCoor = 135,

    /// <summary>
    /// Play a music file.
    /// </summary>
    PlayMusicFile = 136,

    /// <summary>
    /// Start a screen effect.
    /// </summary>
    Effect = 137,

    /// <summary>
    /// Set the map environment.
    /// </summary>
    SetEnvironment = 138,

    /// <summary>
    /// Set the level of graphic detail.
    /// </summary>
    SetGraphDetail = 139,

    /// <summary>
    /// Set the game palette.
    /// </summary>
    SetGamma = 140,

    /// <summary>
    /// Set the wind's strength and direction.
    /// </summary>
    SetWind = 141,

    /// <summary>
    /// Start playing a movie.
    /// </summary>
    PlayMovie = 142,

    /// <summary>
    /// Return whether a movie plays.
    /// </summary>
    IsPlayMovie = 143,

    /// <summary>
    /// Stop the movie.
    /// </summary>
    StopMovie = 144,

    /// <summary>
    /// Return whether music plays.
    /// </summary>
    IsPlayMusic = 145,

    /// <summary>
    /// Blend two palettes.
    /// </summary>
    CountGamma = 146,

    /// <summary>
    /// Return the game palette.
    /// </summary>
    GetGamma = 147,

    /// <summary>
    /// Return how far a screen effect has run, in percent.
    /// </summary>
    GetEffectState = 148,

    /// <summary>
    /// Return the name of the map before this one.
    /// </summary>
    GetPreviousMapName = 149,

    /// <summary>
    /// Return the user name of the copy protection.
    /// </summary>
    GetAsProtectUserName = 150,

    /// <summary>
    /// Return the name of the current map.
    /// </summary>
    GetMapName = 151,

    /// <summary>
    /// Run a program.
    /// </summary>
    Exec = 152,

    /// <summary>
    /// Return a character of a string.
    /// </summary>
    Charat = 153,

    /// <summary>
    /// Write a line to the error log.
    /// </summary>
    Log = 154,

    /// <summary>
    /// Return a random number up to a maximum.
    /// </summary>
    Random = 155,

    /// <summary>
    /// Change the height of every sprite of an object ID.
    /// </summary>
    ChangeZUnit = 156,

    /// <summary>
    /// Return the game time in milliseconds.
    /// </summary>
    GetTime = 157,

    /// <summary>
    /// Return the ground's height at a point.
    /// </summary>
    GetGroundZ = 158,

    /// <summary>
    /// Return the length of a string, the same function as <see cref="Strlen"/> under an older number.
    /// </summary>
    OldStrlen = 159,

    /// <summary>
    /// Make a sprite the one a player slot controls.
    /// </summary>
    SetFlagman = 160,

    /// <summary>
    /// Return the sprite that keeps a sprite of an object ID from standing at a point.
    /// </summary>
    CanPlace = 161,

    /// <summary>
    /// Return a field of an object ID.
    /// </summary>
    GetVidData = 162,

    /// <summary>
    /// Change a field of an object ID.
    /// </summary>
    SetVidData = 163,

    /// <summary>
    /// Convert a number to a string.
    /// </summary>
    Itoa = 164,

    /// <summary>
    /// Return the sine of an angle times 1024.
    /// </summary>
    Sin = 165,

    /// <summary>
    /// Return the cosine of an angle times 1024.
    /// </summary>
    Cos = 166,

    /// <summary>
    /// Return the map's width.
    /// </summary>
    MapSizeX = 167,

    /// <summary>
    /// Return the map's height.
    /// </summary>
    MapSizeY = 168,

    /// <summary>
    /// Destroy every sprite of an object ID.
    /// </summary>
    Genocide = 169,

    /// <summary>
    /// Replace every sprite of an object ID with a sprite of another object ID.
    /// </summary>
    ReplaceUnit = 170,

    /// <summary>
    /// Return the checksum of a string.
    /// </summary>
    Crc32 = 171,

    /// <summary>
    /// Format a string with one value.
    /// </summary>
    Printf = 172,

    /// <summary>
    /// Reload the fields of every object ID from the records.
    /// </summary>
    ReloadVid = 173,

    /// <summary>
    /// Append a variable to a file.
    /// </summary>
    FWrite = 174,

    /// <summary>
    /// Read a variable from a file.
    /// </summary>
    FRead = 175,

    /// <summary>
    /// Open a file.
    /// </summary>
    FOpen = 176,

    /// <summary>
    /// Close a file.
    /// </summary>
    FClose = 177,

    /// <summary>
    /// Create a file.
    /// </summary>
    FCreate = 178,

    /// <summary>
    /// Return whether a file is at its end.
    /// </summary>
    FEof = 179,

    /// <summary>
    /// Return a registry string.
    /// </summary>
    GetReg = 182,

    /// <summary>
    /// Set a registry string.
    /// </summary>
    SetReg = 183,

    /// <summary>
    /// Delete a registry string.
    /// </summary>
    DelReg = 184,

    /// <summary>
    /// Return the registry path of the configuration.
    /// </summary>
    GetDefaultRegPath = 185,

    /// <summary>
    /// Return a parameter of the network game.
    /// </summary>
    GetNetInfo = 192,

    /// <summary>
    /// Send a value over the network.
    /// </summary>
    Send = 193,

    /// <summary>
    /// Return a value received over the network.
    /// </summary>
    Receive = 194,

    /// <summary>
    /// Turn the network game on or off.
    /// </summary>
    SetNet = 195,

    /// <summary>
    /// Return the key a network player pressed.
    /// </summary>
    GetNetKey = 196,

    /// <summary>
    /// Return the mouse button state of a network player.
    /// </summary>
    GetNetInputState = 197,

    /// <summary>
    /// Return the length of a string.
    /// </summary>
    Strlen = 205,

    /// <summary>
    /// Return a string in lower case.
    /// </summary>
    ToLower = 206,

    /// <summary>
    /// Return a string in upper case.
    /// </summary>
    ToUpper = 207,

    /// <summary>
    /// Encode a string in base64.
    /// </summary>
    ToBase64 = 208,

    /// <summary>
    /// Return whether a character is a letter.
    /// </summary>
    IsAlpha = 209,

    /// <summary>
    /// Return a statistic of an army.
    /// </summary>
    GetStatistics = 210,

    /// <summary>
    /// Compute a statistic over an area.
    /// </summary>
    CalcStatistics = 211,

    /// <summary>
    /// Return a property of a train.
    /// </summary>
    TrainProperty = 212,

    /// <summary>
    /// Set the semaphore of a rail.
    /// </summary>
    SetSemaphore = 231,

    /// <summary>
    /// Split a train at a point.
    /// </summary>
    BreakTrain = 239,

    /// <summary>
    /// Start iterating over the trains of an army.
    /// </summary>
    FirstTrain = 240,

    /// <summary>
    /// Continue iterating over trains.
    /// </summary>
    NextTrain = 241,

    /// <summary>
    /// Send a train to patrol to a point.
    /// </summary>
    PatrolEngine = 242,

    /// <summary>
    /// Set a push line on the rails.
    /// </summary>
    SetPushLine = 243,

    /// <summary>
    /// Return the x coordinate of the mouse on the screen.
    /// </summary>
    GetScreenInputX = 244,

    /// <summary>
    /// Return the y coordinate of the mouse on the screen.
    /// </summary>
    GetScreenInputY = 245,

    /// <summary>
    /// Turn the clever enemy attack on or off.
    /// </summary>
    SetCleverEnemyAttack = 246,

    /// <summary>
    /// Turn automatic rebirth on or off.
    /// </summary>
    SetAutoReBirth = 247,

    /// <summary>
    /// Patrol a train between two points.
    /// </summary>
    PatrolEngineToPoints = 248,

    /// <summary>
    /// Set the limit of sprites of an object ID on the map.
    /// </summary>
    AddUnitLimit = 249,

    /// <summary>
    /// Allow or forbid monsters to target neutral trains.
    /// </summary>
    SetEnemyCanAttackNeutralTrains = 250,

    /// <summary>
    /// Set an army's money.
    /// </summary>
    SetMoney = 251,

    /// <summary>
    /// Return an army's money.
    /// </summary>
    GetMoney = 252,

    /// <summary>
    /// Return whether a train can reach a point.
    /// </summary>
    CanMoveEngineTo = 253,

    /// <summary>
    /// Return whether a train can reach a spot to attack an enemy from.
    /// </summary>
    CanAttackEngine = 254
}
