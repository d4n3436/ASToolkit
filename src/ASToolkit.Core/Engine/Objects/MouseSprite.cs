using System.Numerics;
using ASToolkit.Core.Engine.IO;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents the mouse cursor sprite.
/// </summary>
public sealed class MouseSprite : Sprite
{
    private const string CursorFolder = "cursores\\";
    private const string CursorExtension = ".ani";
    private const int LastAnimation = 0x10;
    private const int NoCursor = -1;

    private static readonly string[] CursorNames =
    [
        "arrow", "noammo", "move", "clash", "repair", "attack", "farattack", "select", "nomove", "cycle", "link", "unlink",
        "cantmove", "patrol", "delete", "capture", "mine", "unmine", "small-arrow", "small-noammo", "small-move", "small-taran",
        "small-repair", "small-attack", "small-farattack", "small-select", "small-nomove", "cycle", "small-link", "small-unlink",
        "small-cantmove", "small-patrol", "delete", "small-capture", "small-mine", "small-unmine"
    ];

    private readonly string?[] _cursorFiles = new string?[CursorNames.Length];

    /// <summary>
    /// Initializes a new instance of the <see cref="MouseSprite"/> class, with the system cursor in use and no cursor loaded.
    /// </summary>
    /// <param name="number">The number of the sprite.</param>
    /// <param name="definition">The definition of the sprite.</param>
    /// <param name="position">The position of the sprite.</param>
    /// <param name="army">The army of the sprite.</param>
    /// <param name="hitPoints">The hit points of the sprite.</param>
    /// <param name="direction">The direction of the sprite.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    /// <param name="parent">The sprite that created this sprite.</param>
    public MouseSprite(int number, SpriteDefinition definition, Vector3 position, Army army, int hitPoints, Angle direction = default, Map? map = null, Sprite? parent = null)
        : base(number, definition, position, army, hitPoints, direction, map, parent)
    {
    }

    /// <summary>
    /// Gets a value indicating whether the system draws the cursor rather than this sprite.
    /// </summary>
    public bool IsHardwareCursor { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether the cursor is loaded and showing.
    /// </summary>
    public bool IsCursorLoaded { get; private set; }

    /// <summary>
    /// Gets the cursor file loaded for each animation while the system cursor is loaded; <see langword="null"/> stands for the standard arrow when the file couldn't be found.
    /// </summary>
    public ReadOnlySpan<string?> CursorFiles => _cursorFiles;

    /// <summary>
    /// Gets the animation whose system cursor is showing, or -1 when no system cursor is set.
    /// </summary>
    public int ShownCursor { get; private set; } = NoCursor;

    /// <summary>
    /// Draws the cursor when it is drawn in software and loaded.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="map">The map.</param>
    /// <param name="camera">The top left corner of the view, in map pixels.</param>
    [EngineRoutine(0x00446d20)]
    public override void Draw(Screen screen, Map map, Vector2 camera)
    {
        if (!IsHardwareCursor && IsCursorLoaded)
        {
            base.Draw(screen, map, camera);
        }
    }

    /// <summary>
    /// Loads the cursor, reading a system cursor for each animation from the cursor folder when the system draws the cursor.
    /// </summary>
    /// <param name="fileSystem">The file system the cursor files are read from.</param>
    [EngineRoutine(0x00446d90)]
    public void LoadCursor(IGameFileSystem fileSystem)
    {
        if (IsCursorLoaded)
            return;

        IsCursorLoaded = true;
        ShownCursor = NoCursor;
        if (!IsHardwareCursor)
            return;

        for (int i = 0; i < CursorNames.Length; i++)
        {
            string path = CursorFolder + CursorNames[i] + CursorExtension;
            _cursorFiles[i] = fileSystem.TryRead(path, out _) ? path : null;
        }

        ShownCursor = (int)Animation;
    }

    /// <summary>
    /// Hides the cursor and releases the system cursors.
    /// </summary>
    [EngineRoutine(0x00446ef0)]
    public void TurnCursorOff()
    {
        if (!IsCursorLoaded)
            return;

        IsCursorLoaded = false;
        if (!IsHardwareCursor)
            return;

        ShownCursor = NoCursor;
        Array.Clear(_cursorFiles);
    }

    /// <summary>
    /// Switches to the system cursor.
    /// </summary>
    /// <param name="empty">The empty definition the sprite takes.</param>
    /// <param name="fileSystem">The file system the cursor files are read from.</param>
    [EngineRoutine(0x00446f40)]
    public void UseHardwareCursor(SpriteDefinition empty, IGameFileSystem fileSystem)
    {
        if (IsHardwareCursor)
            return;

        TurnCursorOff();
        ReplaceDefinition(empty);
        IsHardwareCursor = true;
        LoadCursor(fileSystem);
    }

    /// <summary>
    /// Switches to drawing the cursor as this sprite.
    /// </summary>
    /// <param name="definition">The definition of the software cursor.</param>
    /// <param name="fileSystem">The file system the cursor files are read from.</param>
    [EngineRoutine(0x00446f70)]
    public void UseSoftwareCursor(SpriteDefinition definition, IGameFileSystem fileSystem)
    {
        if (!IsHardwareCursor)
            return;

        TurnCursorOff();
        var state = Animation;
        ReplaceDefinition(definition);
        ChangeAnimation(state == SpriteAnimation.Stand ? SpriteAnimation.StopMove : SpriteAnimation.Stand);
        ChangeAnimation(state);
        IsHardwareCursor = false;
        LoadCursor(fileSystem);
    }

    /// <summary>
    /// Changes the cursor to an animation, setting its system cursor when the system draws the cursor.
    /// </summary>
    /// <param name="type">The animation of the cursor.</param>
    [EngineRoutine(0x00446fd0)]
    public void ShowCursor(SpriteAnimation type)
    {
        if (!IsHardwareCursor)
        {
            ChangeAnimation(type);
            return;
        }

        if (Animation != type && IsCursorLoaded)
        {
            ShownCursor = (int)type;
        }

        if ((int)type <= LastAnimation)
        {
            ChangeAnimation(type);
            return;
        }

        SetAnimationValue(type);
    }
}
