using ASToolkit.Engine.IO;
using ASToolkit.Engine.Scripts;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public partial class Map
{
    private const byte FunctionPrefix = (byte)'F';
    private const byte NameSeparator = (byte)'_';
    private const int StateHandlerParameters = 1;
    private const int DestroyParameters = 1;
    private const int CollisionParameters = 2;
    private const int DamageParameters = 3;
    private const int IdDigits = 3;
    private const int LongIdDigits = 4;
    private const int GameWon = 1;

    /// <summary>
    /// Calls a script function with a sprite as its first argument.
    /// </summary>
    /// <param name="function">The function's index.</param>
    /// <param name="sprite">The sprite.</param>
    /// <param name="second">The second argument.</param>
    /// <param name="third">The third argument.</param>
    /// <returns>The function's result.</returns>
    [EngineRoutine(0x0040f430)]
    public int ScriptRun(int function, Sprite? sprite, int second = 0, int third = 0) => _flags.HasFlag(MapFlags.ScriptsDisabled) ? 0 : Script.CallFunction(this, function, GetSpriteHandle(sprite), second, third);

    /// <summary>
    /// Writes a number into a script variable.
    /// </summary>
    /// <param name="variable">The variable's name.</param>
    /// <param name="value">The number.</param>
    /// <param name="element">The element of an array variable.</param>
    public void WriteScriptNumber(ReadOnlySpan<byte> variable, int value, int element = 0)
    {
        int index = Script.Objects.Location(variable);

        if (index >= 0)
        {
            Script.GetSlot(Script.Objects[index].Address + element) = ScriptStack.FromNumber(value);
        }
    }

    /// <summary>
    /// Reads a script variable as a number.
    /// </summary>
    /// <param name="variable">The variable's name.</param>
    /// <param name="element">The element of an array variable.</param>
    /// <returns>The number, or 0 if the variable does not exist.</returns>
    public int ReadScriptNumber(ReadOnlySpan<byte> variable, int element = 0)
    {
        int index = Script.Objects.Location(variable);

        return index < 0 ? 0 : Script.GetSlot(Script.Objects[index].Address + element).GetValue();
    }

    internal int RunCollisionHandler(Sprite sprite, Sprite? other) =>
        sprite.Vid.CollisionHandler >= 0 ? ScriptRun(sprite.Vid.CollisionHandler, sprite, GetSpriteHandle(other)) : 0;

    internal void RunCreationHandler(Sprite sprite)
    {
        int handler = sprite.Vid.StateHandlers[(int)SpriteAnimation.Birth];

        if (handler >= 0)
        {
            ScriptRun(handler, sprite);
        }
    }

    internal void RunDestroyHandler(Sprite sprite)
    {
        if (sprite.Vid.DestroyHandler >= 0)
        {
            ScriptRun(sprite.Vid.DestroyHandler, sprite);
        }
    }
}
