using System.Numerics;
using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

/// <summary>
/// Represents the engine's input state of one frame, as a demo stores it.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 32)]
public readonly struct DemoInput
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DemoInput"/> structure.
    /// </summary>
    /// <param name="flags">The held and pressed controls.</param>
    /// <param name="wheel">The mouse wheel steps, positive away from the player.</param>
    /// <param name="cursor">The cursor in map pixels.</param>
    /// <param name="screenCursor">The cursor in screen pixels.</param>
    /// <param name="key">The key pressed this frame as <c>GetKey</c> returns it, or zero.</param>
    /// <param name="virtualKey">The virtual-key code of the key pressed this frame, or zero.</param>
    public DemoInput(InputFlags flags, int wheel, Vector2 cursor, Vector2 screenCursor, int key, int virtualKey)
    {
        Flags = flags;
        Wheel = wheel;
        Cursor = cursor;
        ScreenCursor = screenCursor;
        Key = key;
        VirtualKey = virtualKey;
    }

    /// <summary>
    /// The held and pressed controls of the input.
    /// </summary>
    [FieldOffset(0)]
    public readonly InputFlags Flags;

    /// <summary>
    /// The mouse wheel steps of the input, positive away from the player.
    /// </summary>
    [FieldOffset(4)]
    public readonly int Wheel;

    /// <summary>
    /// The cursor of the input in map pixels, the screen point plus the camera.
    /// </summary>
    [FieldOffset(8)]
    public readonly Vector2 Cursor;

    /// <summary>
    /// The cursor of the input in screen pixels.
    /// </summary>
    [FieldOffset(16)]
    public readonly Vector2 ScreenCursor;

    /// <summary>
    /// The key pressed this frame as its character, or as its virtual-key code shifted left by 8 when the key doesn't type a character, or zero.
    /// </summary>
    [FieldOffset(24)]
    public readonly int Key;

    /// <summary>
    /// The virtual-key code of the key pressed this frame, or zero.
    /// </summary>
    [FieldOffset(28)]
    public readonly int VirtualKey;
}
