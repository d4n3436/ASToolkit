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

    private static void BindStateHandler(Vid? vid, ReadOnlySpan<byte> state, int function, int parameters)
    {
        if (state.IsEmpty || parameters != StateHandlerParameters || vid is null)
            return;

        int value = state.Length == 1 ? state[0] - '0' : (state[0] - '0') * 10 + state[1] - '0';

        if (value is >= 0 and < ASToolkit.Engine.Vid.StateCount)
        {
            vid.StateHandlers[value] = function;
        }
    }

    private static bool IsDigits(ReadOnlySpan<byte> text)
    {
        foreach (byte value in text)
        {
            if (!CRuntime.IsDigit(value))
                return false;
        }

        return true;
    }

    private void ProcessScriptFunctions(ReadOnlySpan<byte> name, int function, int parameters)
    {
        if (name.Length < IdDigits + 2 || name[0] != FunctionPrefix || !IsDigits(name.Slice(1, IdDigits)))
            return;

        if (name[IdDigits + 1] == NameSeparator)
        {
            var rest = name[(IdDigits + 2)..];
            var vid = FindVid((KnownObject)CRuntime.ParseInteger(name.Slice(1, IdDigits)));

            if (rest.StartsWith("DAMAGE"u8))
            {
                if (parameters == DamageParameters && vid is not null)
                {
                    vid.DamageHandler = function;
                }

                return;
            }

            if (rest.StartsWith("DESTROY"u8))
            {
                if (parameters == DestroyParameters && vid is not null)
                {
                    vid.DestroyHandler = function;
                }

                return;
            }

            if (rest.StartsWith("COLLISION"u8))
            {
                if (parameters == CollisionParameters && vid is not null)
                {
                    vid.CollisionHandler = function;
                }

                return;
            }

            BindStateHandler(vid, rest, function, parameters);

            return;
        }

        if (name.Length > LongIdDigits + 1 && IsDigits(name.Slice(1, LongIdDigits)) && name[LongIdDigits + 1] == NameSeparator)
        {
            BindStateHandler(FindVid((KnownObject)CRuntime.ParseInteger(name.Slice(1, LongIdDigits))), name[(LongIdDigits + 2)..], function, parameters);
        }
    }
}
