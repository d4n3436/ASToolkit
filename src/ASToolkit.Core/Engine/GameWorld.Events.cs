using ASToolkit.Core.Engine.Scripts;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

public sealed partial class GameWorld
{
    private const byte FunctionPrefix = (byte)'F';
    private const byte NameSeparator = (byte)'_';
    private const int StateHandlerParameters = 1;
    private const int DestroyParameters = 1;
    private const int CollisionParameters = 2;
    private const int DamageParameters = 3;
    private const int IdDigits = 3;
    private const int LongIdDigits = 4;
    private const int MainFunction = -1;
    private const int GameWon = 1;
    private static ReadOnlySpan<byte> GameStateVariable => "GameState"u8;
    private static ReadOnlySpan<byte> DamageSuffix => "DAMAGE"u8;
    private static ReadOnlySpan<byte> DestroySuffix => "DESTROY"u8;
    private static ReadOnlySpan<byte> CollisionSuffix => "COLLISION"u8;


    [EngineRoutine(0x0040c960)]
    private void BindScript()
    {
        var objects = Script.Objects;
        for (int i = 0; i < objects.Length; i++)
        {
            ref readonly var entry = ref objects[i];

            if (entry.Kind != ScriptObjectKind.Function)
                continue;

            ProcessScriptFunctions(entry.Name.Span, i, entry.Count);
        }
    }

    private void ProcessScriptFunctions(ReadOnlySpan<byte> name, int function, int parameters)
    {
        if (name.Length < IdDigits + 2 || name[0] != FunctionPrefix || !IsDigits(name.Slice(1, IdDigits)))
            return;

        if (name[IdDigits + 1] == NameSeparator)
        {
            var rest = name[(IdDigits + 2)..];
            var definition = Definitions.Find((KnownObject)ReadDigits(name.Slice(1, IdDigits)));

            if (rest.StartsWith(DamageSuffix))
            {
                if (parameters == DamageParameters && definition is not null)
                {
                    definition.DamageHandler = function;
                }

                return;
            }

            if (rest.StartsWith(DestroySuffix))
            {
                if (parameters == DestroyParameters && definition is not null)
                {
                    definition.DestroyHandler = function;
                }

                return;
            }

            if (rest.StartsWith(CollisionSuffix))
            {
                if (parameters == CollisionParameters && definition is not null)
                {
                    definition.CollisionHandler = function;
                }

                return;
            }

            BindStateHandler(definition, rest, function, parameters);

            return;
        }

        if (name.Length > LongIdDigits + 1 && IsDigits(name.Slice(1, LongIdDigits)) && name[LongIdDigits + 1] == NameSeparator)
        {
            BindStateHandler(Definitions.Find((KnownObject)ReadDigits(name.Slice(1, LongIdDigits))), name[(LongIdDigits + 2)..], function, parameters);
        }
    }

    private static void BindStateHandler(SpriteDefinition? definition, ReadOnlySpan<byte> state, int function, int parameters)
    {
        if (state.IsEmpty || parameters != StateHandlerParameters || definition is null)
            return;

        int value = state.Length == 1 ? state[0] - '0' : (state[0] - '0') * 10 + state[1] - '0';

        if ((uint)value < SpriteDefinition.StateCount)
        {
            definition.StateHandlers[value] = function;
        }
    }

    private static bool IsDigits(ReadOnlySpan<byte> text)
    {
        foreach (byte value in text)
        {
            if (!char.IsAsciiDigit((char)value))
                return false;
        }

        return true;
    }

    private static int ReadDigits(ReadOnlySpan<byte> text)
    {
        int value = 0;
        foreach (byte digit in text)
        {
            value = value * 10 + digit - '0';
        }

        return value;
    }

    /// <summary>
    /// Calls a script function with a sprite as its first argument.
    /// </summary>
    /// <param name="function">The function's index.</param>
    /// <param name="unit">The sprite.</param>
    /// <param name="second">The second argument.</param>
    /// <param name="third">The third argument.</param>
    /// <returns>The function's result.</returns>
    [EngineRoutine(0x0040f430)]
    public int CallScript(int function, Unit unit, int second = 0, int third = 0) => Script.CallFunction(this, function, GetSpriteHandle(unit), second, third);

    [EngineRoutine(0x00440110)]
    private bool RunStateHandlers(Unit unit, bool everyFrame)
    {
        var state = unit.Animation;
        int handler = unit.Definition.StateHandlers[(int)state];
        while (handler >= 0 && (unit.Frame == unit.FirstFrame || everyFrame))
        {
            if (CallScript(handler, unit) != 0)
                return true;

            if (unit.Animation == state)
                break;

            state = unit.Animation;
            handler = unit.Definition.StateHandlers[(int)state];
        }

        return false;
    }

    [EngineRoutine(0x0040e870)]
    private void RunCreationHandler(Unit unit)
    {
        int handler = unit.Definition.StateHandlers[(int)SpriteAnimation.Birth];

        if (handler >= 0)
        {
            CallScript(handler, unit);
        }
    }

    [EngineRoutine(0x0043f520)]
    private void RunDestroyHandler(Unit unit)
    {
        if (unit.Definition.DestroyHandler >= 0)
        {
            CallScript(unit.Definition.DestroyHandler, unit);
        }
    }

    private bool IsLevelWon() => ReadScriptNumber(GameStateVariable) == GameWon;

    /// <summary>
    /// Writes a number into a script variable.
    /// </summary>
    /// <param name="variable">The variable's name.</param>
    /// <param name="value">The number.</param>
    /// <param name="element">The element of an array variable.</param>
    public void WriteScriptNumber(ReadOnlySpan<byte> variable, int value, int element = 0)
    {
        int index = Script.FindObject(variable);

        if (index >= 0)
        {
            Script.GetSlot(Script.Objects[index].Address + element) = StackObject.FromNumber(value);
        }
    }

    /// <summary>
    /// Reads a script variable as a number.
    /// </summary>
    /// <param name="variable">The variable's name.</param>
    /// <param name="element">The element of an array variable.</param>
    /// <returns>The number, or 0 when the script has no such variable.</returns>
    public int ReadScriptNumber(ReadOnlySpan<byte> variable, int element = 0)
    {
        int index = Script.FindObject(variable);

        return index < 0 ? 0 : Script.GetSlot(Script.Objects[index].Address + element).GetValue();
    }

    [EngineRoutine(0x00405190)]
    private void RunMain() => Script.CallFunction(this, MainFunction, 0, 0, 0);
}
