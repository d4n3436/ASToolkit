using System.Buffers.Binary;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine.Scripts;

/// <summary>
/// Represents a compiled script.
/// </summary>
public sealed partial class Script
{
    private const int MinCapacity = 0x80;

    private readonly MyList<ScriptStack> _stack = new();
    private readonly NamedList<ScriptObject> _objects = new();
    private byte[]? _code;

    /// <summary>
    /// Initializes a new instance of the <see cref="Script"/> class as an empty script in Alien Shooter's version of the script language.
    /// </summary>
    public Script() : this(ScriptVersion.AlienShooter)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Script"/> class as an empty script in a version of the script language.
    /// </summary>
    /// <param name="version">The version of the script language the script is compiled in.</param>
    public Script(ScriptVersion version)
    {
        Version = version;
    }

    /// <summary>
    /// Gets the version of the script language the script is compiled in.
    /// </summary>
    public ScriptVersion Version { get; }

    /// <summary>
    /// Gets the index of the object named <c>main</c>, or -1 if the script doesn't have one.
    /// </summary>
    public int MainIndex { get; private set; } = -1;

    /// <summary>
    /// Gets the number of variable slots, or the stack height while the script runs.
    /// </summary>
    public int VariableCount => _stack.Count;

    internal MyList<ScriptStack> Stack => _stack;

    /// <summary>
    /// Gets the names of the script.
    /// </summary>
    public NamedList<ScriptObject> Objects => _objects;

    /// <summary>
    /// Gets the compiled code.
    /// </summary>
    public ReadOnlySpan<byte> Code => _code;

    /// <summary>
    /// Gets a value indicating whether the value on top of the stack is text.
    /// </summary>
    public bool IsLastStackString
    {
        [EngineRoutine(0x0043a190)]
        get => _stack[_stack.Count - 1].Flags.HasFlag(ScriptStackFlags.Text);
    }

    internal MyError? Error
    {
        get;
        set
        {
            field = value;
            _objects.Error = value;
            _defines.Error = value;
        }
    }

    private bool IsAlienShooter2 => Version == ScriptVersion.AlienShooter2;

    /// <summary>
    /// Returns the address of a slot's text as a number, never 0.
    /// </summary>
    /// <param name="slot">The index of the slot.</param>
    /// <returns>The address.</returns>
    public static int GetTextAddress(int slot) => slot + 1;

    /// <summary>
    /// Returns a variable or stack slot.
    /// </summary>
    /// <param name="index">The index of the slot.</param>
    /// <returns>The slot.</returns>
    public ref ScriptStack GetSlot(int index) => ref _stack[index];


    /// <summary>
    /// Returns the code offset where a function's code ends, at the next function's start.
    /// </summary>
    /// <param name="index">The index of the function's object.</param>
    /// <returns>The end offset.</returns>
    public int GetFunctionEnd(int index)
    {
        int start = _objects[index].Address;
        int end = _code?.Length ?? 0;
        for (int other = 0; other < _objects.Count; other++)
        {
            ref readonly var entry = ref _objects[other];
            if (entry.Kind == ScriptObjectKind.Function && entry.Address > start && entry.Address < end)
            {
                end = entry.Address;
            }
        }

        return end;
    }

    /// <summary>
    /// Returns the operand of a call instruction to a function.
    /// </summary>
    /// <param name="function">The index of the function's object.</param>
    /// <returns>The operand.</returns>
    public int GetCallOperand(int function) => IsAlienShooter2 ? function : _objects[function].Address;

    /// <summary>
    /// Finds the function that the operand of a call instruction names.
    /// </summary>
    /// <param name="operand">The operand of the call instruction.</param>
    /// <returns>The index of the function's object, or -1 if there isn't one.</returns>
    public int FindCalledFunction(int operand)
    {
        if (IsAlienShooter2)
            return operand >= 0 && operand < _objects.Count && _objects[operand].Kind == ScriptObjectKind.Function ? operand : -1;

        for (int i = 0; i < _objects.Count; i++)
        {
            if (_objects[i].Kind == ScriptObjectKind.Function && _objects[i].Address == operand)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Pops a value from the stack as a number.
    /// </summary>
    /// <returns>The number.</returns>
    public int PopNumber() => _stack.Pop().GetValue();

    /// <summary>
    /// Pops a value from the stack as a sprite handle.
    /// </summary>
    /// <returns>The handle, or 0 if there is no handle. A nonzero number that isn't a sprite handle is logged.</returns>
    [EngineRoutine(0x00439fd0)]
    public int PopHandle()
    {
        ref readonly var slot = ref _stack[_stack.Count - 1];
        if (slot.Value != 0 && !slot.Flags.HasFlag(ScriptStackFlags.Sprite))
        {
            Error?.Error("LOGIC"u8, LogError.Text, 0, "this variable is not unit"u8);
        }

        return _stack.Pop().GetValue();
    }

    /// <summary>
    /// Pops a value from the stack as text, with a number written in decimal.
    /// </summary>
    /// <returns>The text.</returns>
    public ReadOnlyMemory<byte> PopText() => _stack.Pop().GetText();

    /// <summary>
    /// Pushes a text onto the stack.
    /// </summary>
    /// <param name="text">The text.</param>
    [EngineRoutine(0x00439ef0)]
    public void PushText(ReadOnlyMemory<byte> text) => _stack.Insert(ScriptStack.FromText(text));

    /// <summary>
    /// Clears every variable that holds the handle of a deleted sprite.
    /// </summary>
    /// <param name="handle">The handle of the sprite.</param>
    /// <returns>The number of variables cleared.</returns>
    [EngineRoutine(0x00424460)]
    public int DeletePointerToObject(int handle)
    {
        int cleared = 0;
        for (int i = 0; i < _stack.Count; i++)
        {
            ref var slot = ref _stack[i];
            if (slot.Flags.HasFlag(ScriptStackFlags.Sprite) && slot.Value == handle)
            {
                slot.Value = 0;
                slot.Flags &= ~ScriptStackFlags.Sprite;
                cleared++;
            }
        }

        return cleared;
    }

    /// <summary>
    /// Pushes a number onto the stack.
    /// </summary>
    /// <param name="value">The number.</param>
    [EngineRoutine(0x0043a060)]
    public void PushInt(int value) => _stack.Insert(ScriptStack.FromNumber(value));

    /// <summary>
    /// Pushes a sprite handle onto the stack.
    /// </summary>
    /// <param name="value">The handle, or 0 if there is no handle.</param>
    [EngineRoutine(0x0043a0f0)]
    public void PushSprite(int value) => _stack.Insert(ScriptStack.FromHandle(value));

    /// <summary>
    /// Runs a function of the script.
    /// </summary>
    /// <param name="host">The object that runs the built-in functions.</param>
    /// <param name="function">The index of the function, or a negative index for <c>main</c>.</param>
    /// <param name="first">The first argument, a sprite handle when nonzero.</param>
    /// <param name="second">The second argument, a sprite handle when nonzero.</param>
    /// <param name="third">The third argument.</param>
    /// <returns>1 if the function destroys the sprite of its first argument, otherwise the value of the outermost <c>return</c>, otherwise 0.</returns>
    /// <exception cref="NotSupportedException">The script is an Alien Shooter 2 script.</exception>
    /// <exception cref="InvalidOperationException">The function pops more values than it pushes.</exception>
    [EngineRoutine(0x00423430)]
    public int CallFunction(IScriptFunctionHost host, int function, int first, int second, int third)
    {
        if (IsAlienShooter2)
            throw new NotSupportedException("Alien Shooter 2 scripts are not supported.");

        if (_code is null)
            return 0;

        if (function < 0)
        {
            function = MainIndex;
        }

        if (!CanCallFunction(function))
            return 0;

        ref readonly var entry = ref _objects[function];
        int savedCount = _stack.Count;
        PushInt(savedCount);
        PushInt(_code.Length);
        SetArgument(entry, 0, first, true);
        SetArgument(entry, 1, second, true);
        SetArgument(entry, 2, third, false);
        int result = RunFunction(host, function, first, _code, entry.Address);
        RestoreStack(savedCount);

        return result;
    }

    private static void Step(ref ScriptStack slot, int amount)
    {
        slot.Flags &= ~ScriptStackFlags.Sprite;
        if ((slot.Flags & (ScriptStackFlags.Number | ScriptStackFlags.Pointer)) != 0)
        {
            slot.Value += amount;
        }
    }

    private static void StoreCharacter(ref ScriptStack slot, int index, int character)
    {
        if ((byte)character == 0)
        {
            if (index < slot.Text.Length)
            {
                slot.Text = slot.Text[..index];
            }

            return;
        }

        byte[] text = slot.Text.ToArray();
        text[index] = (byte)character;
        slot.Text = text;
    }

    private bool CanCallFunction(int function)
    {
        if (function >= _objects.Count || function < 0)
        {
            Error?.Write($"!!!ERROR!!! SCRIPT Call unexisted function {function}");

            return false;
        }

        ref readonly var entry = ref _objects[function];
        if (entry.Kind != ScriptObjectKind.Function)
        {
            Error?.Write($"!!!ERROR!!!LOGIC: Call unexisted function {MyError.Decode(_objects.Name(function).Span)}()");

            return false;
        }

        return true;
    }

    private void SetArgument(in ScriptObject entry, int parameter, int value, bool isHandle)
    {
        if (entry.Count > parameter)
        {
            _stack[entry.FirstVariable + parameter] = new ScriptStack(isHandle && value != 0 ? ScriptStackFlags.Number | ScriptStackFlags.Sprite : ScriptStackFlags.Number, value);
        }
    }

    private void SetTop(int value)
    {
        _stack[_stack.Count - 1].Value = value;
        _stack[_stack.Count - 1].Flags = ScriptStackFlags.Number;
    }

    private void RestoreStack(int count)
    {
        _stack.Expand(count);
        _stack.Count = count;
    }

    private void EndStatement(string name, int frame, int programCounter)
    {
        if (_stack.Count - frame > 1)
        {
            Error?.Write($"!!!ERROR!!!LOGIC: '{name}' stack error {programCounter}");
        }

        RestoreStack(frame);
    }

    private int RunFunction(IScriptFunctionHost host, int function, int first, byte[] code, int programCounter)
    {
        int result = 0;
        bool indexPending = false;
        int index = 0;
        int frame = _stack.Count;
        int statement = programCounter;
        while (programCounter < code.Length)
        {
            if (_stack.Count < frame)
            {
                Error?.Write($"!!!ERROR!!!LOGIC: 'pop, but not push' stack error {programCounter}");

                throw new InvalidOperationException($"Script function {function} pops more values than it pushes, at address {programCounter}.");
            }

            var opcode = (ScriptOpcode)code[programCounter];
            if (opcode is >= ScriptOpcode.Divide and <= ScriptOpcode.ShiftLeft)
            {
                programCounter++;
                BinarOperator(ref _stack[_stack.Count - 2], opcode, _stack[_stack.Count - 1]);
                _stack.Count--;
                continue;
            }

            if (opcode is >= ScriptOpcode.PostIncrement and <= ScriptOpcode.CompoundAssign)
            {
                programCounter = RunVariableOp(code, programCounter, indexPending, index);
                indexPending = false;
                index = 0;
                continue;
            }

            programCounter++;
            switch (opcode)
            {
                case ScriptOpcode.PushNumber:
                    PushInt(BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(programCounter)));
                    programCounter += 4;
                    break;
                case ScriptOpcode.PushText:
                    int length = code.AsSpan(programCounter).IndexOf((byte)0);
                    _stack.Insert(ScriptStack.FromText(code.AsMemory(programCounter, length)));
                    programCounter += length + 1;
                    break;
                case ScriptOpcode.Negate:
                    SetTop(-_stack[_stack.Count - 1].GetValue());
                    break;
                case ScriptOpcode.BitwiseNot:
                    SetTop(~_stack[_stack.Count - 1].GetValue());
                    break;
                case ScriptOpcode.LogicalNot:
                    SetTop(_stack[_stack.Count - 1].GetValue() == 0 ? 1 : 0);
                    break;
                case ScriptOpcode.JumpIfFalse:
                    programCounter = RunJumpIfFalse(code, programCounter);
                    EndStatement("if", frame, programCounter);
                    statement = programCounter;
                    break;
                case ScriptOpcode.EndStatement:
                    programCounter += 4;
                    EndStatement(";", frame, programCounter);
                    statement = programCounter;
                    break;
                case ScriptOpcode.Pop:
                    _stack.Count--;
                    break;
                case ScriptOpcode.Jump:
                    programCounter += BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(programCounter));
                    break;
                case ScriptOpcode.JumpIfFalseOnce:
                    programCounter = RunJumpIfFalseOnce(code, programCounter, statement);
                    EndStatement("iff", frame, programCounter);
                    statement = programCounter;
                    break;
                case ScriptOpcode.Call:
                    PushInt(frame);
                    PushInt(programCounter + 4);
                    programCounter = BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(programCounter));
                    frame = _stack.Count;
                    statement = programCounter;
                    break;
                case ScriptOpcode.Return:
                    RunReturn(code.Length, ref programCounter, ref frame, ref result);
                    statement = programCounter;
                    break;
                case ScriptOpcode.SetIndex:
                    _stack.Count--;
                    index = _stack[_stack.Count].GetValue();
                    indexPending = true;
                    break;
                default:
                    if (code[programCounter - 1] == (byte)ScriptFunction.Destroy && _stack[_stack.Count - 1].GetValue() == first)
                    {
                        result = 1;
                    }

                    host.ExecFunc(this, code[programCounter - 1]);
                    break;
            }
        }

        return result;
    }

    private int RunVariableOp(byte[] code, int programCounter, bool indexPending, int index)
    {
        var opcode = (ScriptOpcode)code[programCounter];
        int operand = BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(programCounter + 1));
        int target = operand;
        if (_stack[operand].Flags.HasFlag(ScriptStackFlags.Pointer) && indexPending)
        {
            target = _stack[operand].Value;
        }

        target += index;
        programCounter++;
        switch (opcode)
        {
            case ScriptOpcode.PostIncrement:
                _stack.Insert(_stack[target]);
                Step(ref _stack[target], 1);
                break;
            case ScriptOpcode.PostDecrement:
                _stack.Insert(_stack[target]);
                Step(ref _stack[target], -1);
                break;
            case ScriptOpcode.PreIncrement:
                Step(ref _stack[target], 1);
                _stack.Insert(_stack[target]);
                break;
            case ScriptOpcode.PreDecrement:
                Step(ref _stack[target], -1);
                _stack.Insert(_stack[target]);
                break;
            case ScriptOpcode.Load:
                if (!indexPending && _stack[target].Flags.HasFlag(ScriptStackFlags.ArrayElement))
                {
                    PushInt(BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(programCounter)));
                }
                else
                {
                    _stack.Insert(_stack[target]);
                }

                break;
            case ScriptOpcode.LoadTextAddress:
                PushInt(_stack[target].Flags.HasFlag(ScriptStackFlags.Text) ? GetTextAddress(target) : 0);
                break;
            case ScriptOpcode.Assign:
                Assign(operand, target, indexPending, index);
                break;
            case ScriptOpcode.CompoundAssign:
                _stack.Count--;
                BinarOperator(ref _stack[target], (ScriptOpcode)code[programCounter + 4], _stack[_stack.Count]);
                _stack.Insert(_stack[target]);
                programCounter++;
                break;
        }

        return programCounter + 4;
    }

    private void Assign(int operand, int target, bool indexPending, int index)
    {
        if (!_stack[operand].Flags.HasFlag(ScriptStackFlags.Text))
        {
            _stack[target].Flags &= ~(ScriptStackFlags.Sprite | ScriptStackFlags.NotInitialized);
            _stack[target].Value = _stack[_stack.Count - 1].GetValue();
            if (_stack[_stack.Count - 1].Flags.HasFlag(ScriptStackFlags.Sprite))
            {
                _stack[target].Flags |= ScriptStackFlags.Sprite;
            }
        }
        else if (indexPending && !_stack[operand].Flags.HasFlag(ScriptStackFlags.ArrayElement))
        {
            StoreCharacter(ref _stack[operand], index, _stack[_stack.Count - 1].GetValue());
        }
        else
        {
            _stack[target].Text = _stack[_stack.Count - 1].Flags.HasFlag(ScriptStackFlags.Number) ? ScriptStack.FormatNumber(_stack[_stack.Count - 1].Value) : _stack[_stack.Count - 1].Text;
        }
    }

    private int RunJumpIfFalse(byte[] code, int programCounter)
    {
        _stack.Count--;
        if (_stack[_stack.Count].GetValue() == 0)
            return programCounter + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(programCounter));

        return programCounter + 4;
    }

    private int RunJumpIfFalseOnce(byte[] code, int programCounter, int statement)
    {
        _stack.Count--;
        if (_stack[_stack.Count].GetValue() == 0)
            return programCounter + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(programCounter));

        code[statement] = (byte)ScriptOpcode.Jump;
        BinaryPrimitives.WriteInt32LittleEndian(code.AsSpan(statement + 1), BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(programCounter)) - statement + programCounter - 1);

        return programCounter + 4;
    }

    private void RunReturn(int codeLength, ref int programCounter, ref int frame, ref int result)
    {
        if (_stack.Count - frame > 1)
        {
            Error?.Write($"!!!ERROR!!!LOGIC: 'return' stack error {programCounter}");
        }

        bool hasValue = _stack.Count > frame;
        var returned = hasValue ? _stack.Pop() : default;
        RestoreStack(frame);
        programCounter = _stack.Pop().GetValue();
        frame = _stack.Pop().GetValue();
        if (hasValue)
        {
            _stack.Insert(returned);
            if (programCounter >= codeLength && result == 0)
            {
                result = returned.GetValue();
            }
        }
    }

    [EngineRoutine(0x004245e0)]
    private void BinarOperator(ref ScriptStack left, ScriptOpcode opcode, in ScriptStack right)
    {
        int value = right.GetValue();
        bool areTexts = left.Flags.HasFlag(ScriptStackFlags.Text) && right.Flags.HasFlag(ScriptStackFlags.Text);
        if (left.Flags.HasFlag(ScriptStackFlags.Text))
        {
            left.Value = ScriptStack.ParseNumber(left.Text.Span);
        }

        int current = left.Value;
        switch (opcode)
        {
            case ScriptOpcode.Divide:
                left.Value = value == 0 ? 0x0fffffff : current / value;
                break;
            case ScriptOpcode.Modulo:
                left.Value = current % value;
                break;
            case ScriptOpcode.Add when areTexts:
                byte[] joined = GC.AllocateUninitializedArray<byte>(left.Text.Length + right.Text.Length);
                left.Text.Span.CopyTo(joined);
                right.Text.Span.CopyTo(joined.AsSpan(left.Text.Length));
                left.Text = joined;
                left.Flags = ScriptStackFlags.Text;
                return;
            case ScriptOpcode.Add:
                left.Value = unchecked(current + value);
                break;
            case ScriptOpcode.Subtract:
                left.Value = unchecked(current - value);
                break;
            case ScriptOpcode.Xor:
                left.Value = current ^ value;
                break;
            case ScriptOpcode.Or:
                left.Value = current | value;
                break;
            case ScriptOpcode.And:
                left.Value = current & value;
                break;
            case ScriptOpcode.Equal:
                left.Value = (areTexts ? left.Text.Span.SequenceEqual(right.Text.Span) : current == value) ? 1 : 0;
                break;
            case ScriptOpcode.LogicalOr:
                left.Value = current == 0 && value == 0 ? 0 : 1;
                break;
            case ScriptOpcode.Greater:
                left.Value = current > value ? 1 : 0;
                break;
            case ScriptOpcode.Less:
                left.Value = current < value ? 1 : 0;
                break;
            case ScriptOpcode.GreaterOrEqual:
                left.Value = current >= value ? 1 : 0;
                break;
            case ScriptOpcode.LessOrEqual:
                left.Value = current <= value ? 1 : 0;
                break;
            case ScriptOpcode.Multiply:
                left.Value = unchecked(current * value);
                break;
            case ScriptOpcode.NotEqual:
                left.Value = (areTexts ? !left.Text.Span.SequenceEqual(right.Text.Span) : current != value) ? 1 : 0;
                break;
            case ScriptOpcode.LogicalAnd:
                left.Value = current != 0 && value != 0 ? 1 : 0;
                break;
            case ScriptOpcode.ShiftRight:
                left.Value = current >> (value & 31);
                break;
            case ScriptOpcode.ShiftLeft:
                left.Value = current << (value & 31);
                break;
            default:
                Error?.Write($"!!!ERROE!!!LOGIC::Unknown Binary command {(int)opcode}");
                break;
        }

        left.Flags = ScriptStackFlags.Number;
    }
}
