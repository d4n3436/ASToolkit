using System.Buffers.Binary;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine.Scripts;

/// <summary>
/// Represents a compiled script.
/// </summary>
public sealed partial class Script
{
    private const byte VariableElementStart = (byte)'[';
    private const int MinCapacity = 0x80;

    private readonly MyList<ScriptStack> _stack = new();
    private readonly NamedList<ScriptObject> _objects = new();
    private readonly IGameFileSystem _fileSystem;
    private byte[]? _code;

    /// <summary>
    /// Initializes a new instance of the <see cref="Script"/> class as an empty script in Alien Shooter's version of the script language.
    /// </summary>
    /// <param name="fileSystem">The file system that holds the script and its includes.</param>
    public Script(IGameFileSystem fileSystem) : this(ScriptVersion.AlienShooter, fileSystem)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Script"/> class as an empty script in a version of the script language.
    /// </summary>
    /// <param name="version">The version of the script language the script is compiled in.</param>
    /// <param name="fileSystem">The file system that holds the script and its includes.</param>
    public Script(ScriptVersion version, IGameFileSystem fileSystem)
    {
        Version = version;
        _fileSystem = fileSystem;
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
        get => _stack[^1].Flags.HasFlag(ScriptStackFlags.Text);
    }

    /// <summary>
    /// Gets or sets the object that runs the built-in functions.
    /// </summary>
    public IScriptFunctionHost? Host { get; set; }

    internal MyList<ScriptStack> Stack => _stack;

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
    /// Returns a variable or stack slot.
    /// </summary>
    /// <param name="index">The index of the slot.</param>
    /// <returns>The slot.</returns>
    public ref ScriptStack GetSlot(int index) => ref _stack[index];

    /// <summary>
    /// Pops a value from the stack as text, with a number written in decimal.
    /// </summary>
    /// <returns>The text.</returns>
    public ReadOnlyMemory<byte> PopText() => _stack.Pop().GetText();

    /// <summary>
    /// Runs a function of the script.
    /// </summary>
    /// <param name="function">The index of the function, or a negative index for <c>main</c>.</param>
    /// <param name="first">The first argument, a sprite handle when nonzero.</param>
    /// <param name="second">The second argument, a sprite handle when nonzero.</param>
    /// <param name="third">The third argument.</param>
    /// <returns>1 if the function destroys the sprite of its first argument, otherwise the value of the outermost <c>return</c>, otherwise 0.</returns>
    /// <exception cref="NotSupportedException">The script is an Alien Shooter 2 script.</exception>
    /// <exception cref="InvalidOperationException">The function pops more values than it pushes.</exception>
    [EngineRoutine(0x00423430)]
    public int CallFunction(int function, int first, int second, int third)
    {
        if (IsAlienShooter2)
            throw new NotSupportedException("Alien Shooter 2 scripts are not supported.");

        if (_code is null)
            return 0;

        if (function < 0)
        {
            function = MainIndex;
        }

        if (function >= _objects.Count || function < 0)
        {
            Error?.Log($"!!!ERROR!!! SCRIPT Call unexisted function {function}");

            return 0;
        }

        ref readonly var entry = ref _objects[function];
        if (entry.Kind != ScriptObjectKind.Function)
        {
            Error?.Log($"!!!ERROR!!!LOGIC: Call unexisted function {MyError.Decode(_objects.Name(function).Span)}()");

            return 0;
        }

        int savedCount = _stack.Count;
        _stack.Push(ScriptStack.FromNumber(savedCount));
        _stack.Push(ScriptStack.FromNumber(_code.Length));
        if (entry.Count > 0)
        {
            _stack[entry.FirstVariable] = new ScriptStack(first != 0 ? ScriptStackFlags.Number | ScriptStackFlags.Sprite : ScriptStackFlags.Number, first);
        }

        if (entry.Count > 1)
        {
            _stack[entry.FirstVariable + 1] = new ScriptStack(second != 0 ? ScriptStackFlags.Number | ScriptStackFlags.Sprite : ScriptStackFlags.Number, second);
        }

        if (entry.Count > 2)
        {
            _stack[entry.FirstVariable + 2] = new ScriptStack(ScriptStackFlags.Number, third);
        }

        byte[] code = _code;
        int programCounter = entry.Address;
        int result = 0;
        bool indexPending = false;
        int index = 0;
        int frame = _stack.Count;
        int statement = programCounter;
        while (programCounter < code.Length)
        {
            if (_stack.Count < frame)
            {
                Error?.Log($"!!!ERROR!!!LOGIC: 'pop, but not push' stack error {programCounter}");

                throw new InvalidOperationException($"Script function {function} pops more values than it pushes, at address {programCounter}.");
            }

            var opcode = (ScriptOpcode)code[programCounter];
            if (opcode is >= ScriptOpcode.Divide and <= ScriptOpcode.ShiftLeft)
            {
                programCounter++;
                if (!_stack[^2].BinarOperator(opcode, _stack[^1]))
                {
                    Error?.Log($"!!!ERROE!!!LOGIC::Unknown Binary command {(int)opcode}");
                }

                _stack.Count--;
                continue;
            }

            if (opcode is >= ScriptOpcode.PostIncrement and <= ScriptOpcode.CompoundAssign)
            {
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
                        _stack[target].Flags &= ~ScriptStackFlags.Sprite;
                        if ((_stack[target].Flags & (ScriptStackFlags.Number | ScriptStackFlags.Pointer)) != 0)
                        {
                            _stack[target].Value += 1;
                        }
                        break;
                    case ScriptOpcode.PostDecrement:
                        _stack.Insert(_stack[target]);
                        _stack[target].Flags &= ~ScriptStackFlags.Sprite;
                        if ((_stack[target].Flags & (ScriptStackFlags.Number | ScriptStackFlags.Pointer)) != 0)
                        {
                            _stack[target].Value += -1;
                        }
                        break;
                    case ScriptOpcode.PreIncrement:
                        _stack[target].Flags &= ~ScriptStackFlags.Sprite;
                        if ((_stack[target].Flags & (ScriptStackFlags.Number | ScriptStackFlags.Pointer)) != 0)
                        {
                            _stack[target].Value += 1;
                        }
                        _stack.Insert(_stack[target]);
                        break;
                    case ScriptOpcode.PreDecrement:
                        _stack[target].Flags &= ~ScriptStackFlags.Sprite;
                        if ((_stack[target].Flags & (ScriptStackFlags.Number | ScriptStackFlags.Pointer)) != 0)
                        {
                            _stack[target].Value += -1;
                        }
                        _stack.Insert(_stack[target]);
                        break;
                    case ScriptOpcode.Load:
                        if (!indexPending && _stack[target].Flags.HasFlag(ScriptStackFlags.ArrayElement))
                        {
                            _stack.Push(ScriptStack.FromNumber(BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(programCounter))));
                        }
                        else
                        {
                            _stack.Insert(_stack[target]);
                        }

                        break;
                    case ScriptOpcode.LoadTextAddress:
                        _stack.Push(ScriptStack.FromNumber(_stack[target].Flags.HasFlag(ScriptStackFlags.Text) ? target + 1 : 0));
                        break;
                    case ScriptOpcode.Assign:
                        if (!_stack[operand].Flags.HasFlag(ScriptStackFlags.Text))
                        {
                            _stack[target].Flags &= ~(ScriptStackFlags.Sprite | ScriptStackFlags.NotInitialized);
                            _stack[target].Value = _stack[^1].GetValue();
                            if (_stack[^1].Flags.HasFlag(ScriptStackFlags.Sprite))
                            {
                                _stack[target].Flags |= ScriptStackFlags.Sprite;
                            }
                        }
                        else if (indexPending && !_stack[operand].Flags.HasFlag(ScriptStackFlags.ArrayElement))
                        {
                            StoreCharacter(ref _stack[operand], index, _stack[^1].GetValue());
                        }
                        else
                        {
                            _stack[target].Text = _stack[^1].Flags.HasFlag(ScriptStackFlags.Number) ? ScriptStack.FormatNumber(_stack[^1].Value) : _stack[^1].Text;
                        }
                        break;
                    case ScriptOpcode.CompoundAssign:
                        _stack.Count--;
                        var compound = (ScriptOpcode)code[programCounter + 4];
                        if (!_stack[target].BinarOperator(compound, _stack[_stack.Count]))
                        {
                            Error?.Log($"!!!ERROE!!!LOGIC::Unknown Binary command {(int)compound}");
                        }

                        _stack.Insert(_stack[target]);
                        programCounter++;
                        break;
                }

                programCounter += 4;
                indexPending = false;
                index = 0;
                continue;
            }

            programCounter++;
            switch (opcode)
            {
                case ScriptOpcode.PushNumber:
                    _stack.Push(ScriptStack.FromNumber(BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(programCounter))));
                    programCounter += 4;
                    break;
                case ScriptOpcode.PushText:
                    int length = code.AsSpan(programCounter).IndexOf((byte)0);
                    _stack.Push(ScriptStack.FromText(code.AsMemory(programCounter, length)));
                    programCounter += length + 1;
                    break;
                case ScriptOpcode.Negate:
                    _stack[^1].Value = -_stack[^1].GetValue();
                    _stack[^1].Flags = ScriptStackFlags.Number;
                    break;
                case ScriptOpcode.BitwiseNot:
                    _stack[^1].Value = ~_stack[^1].GetValue();
                    _stack[^1].Flags = ScriptStackFlags.Number;
                    break;
                case ScriptOpcode.LogicalNot:
                    _stack[^1].Value = _stack[^1].GetValue() == 0 ? 1 : 0;
                    _stack[^1].Flags = ScriptStackFlags.Number;
                    break;
                case ScriptOpcode.JumpIfFalse:
                    _stack.Count--;
                    programCounter += _stack[_stack.Count].GetValue() == 0 ? BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(programCounter)) : 4;
                    if (_stack.Count - frame > 1)
                    {
                        Error?.Log($"!!!ERROR!!!LOGIC: 'if' stack error {programCounter}");
                    }

                    RestoreStack(frame);
                    statement = programCounter;
                    break;
                case ScriptOpcode.EndStatement:
                    programCounter += 4;
                    if (_stack.Count - frame > 1)
                    {
                        Error?.Log($"!!!ERROR!!!LOGIC: ';' stack error {programCounter}");
                    }

                    RestoreStack(frame);
                    statement = programCounter;
                    break;
                case ScriptOpcode.Pop:
                    _stack.Count--;
                    break;
                case ScriptOpcode.Jump:
                    programCounter += BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(programCounter));
                    break;
                case ScriptOpcode.JumpIfFalseOnce:
                    _stack.Count--;
                    if (_stack[_stack.Count].GetValue() == 0)
                    {
                        programCounter += BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(programCounter));
                    }
                    else
                    {
                        code[statement] = (byte)ScriptOpcode.Jump;
                        BinaryPrimitives.WriteInt32LittleEndian(code.AsSpan(statement + 1), BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(programCounter)) - statement + programCounter - 1);
                        programCounter += 4;
                    }

                    if (_stack.Count - frame > 1)
                    {
                        Error?.Log($"!!!ERROR!!!LOGIC: 'iff' stack error {programCounter}");
                    }

                    RestoreStack(frame);
                    statement = programCounter;
                    break;
                case ScriptOpcode.Call:
                    _stack.Push(ScriptStack.FromNumber(frame));
                    _stack.Push(ScriptStack.FromNumber(programCounter + 4));
                    programCounter = BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(programCounter));
                    frame = _stack.Count;
                    statement = programCounter;
                    break;
                case ScriptOpcode.Return:
                    if (_stack.Count - frame > 1)
                    {
                        Error?.Log($"!!!ERROR!!!LOGIC: 'return' stack error {programCounter}");
                    }

                    bool hasValue = _stack.Count > frame;
                    var returned = hasValue ? _stack.Pop() : default;
                    RestoreStack(frame);
                    programCounter = _stack.Pop().GetValue();
                    frame = _stack.Pop().GetValue();
                    if (hasValue)
                    {
                        _stack.Insert(returned);
                    }

                    if (hasValue && programCounter >= code.Length && result == 0)
                    {
                        result = returned.GetValue();
                    }

                    statement = programCounter;
                    break;
                case ScriptOpcode.SetIndex:
                    _stack.Count--;
                    index = _stack[_stack.Count].GetValue();
                    indexPending = true;
                    break;
                default:
                    if (code[programCounter - 1] == (byte)ScriptFunction.Destroy && _stack[^1].GetValue() == first)
                    {
                        result = 1;
                    }

                    Host!.ExecFunc(this, code[programCounter - 1]);
                    break;
            }
        }

        RestoreStack(savedCount);

        return result;
    }

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
    /// Pushes a text onto the stack.
    /// </summary>
    /// <param name="text">The text.</param>
    [EngineRoutine(0x00439ef0)]
    public void PushText(ReadOnlyMemory<byte> text) => _stack.Insert(ScriptStack.FromText(text));

    /// <summary>
    /// Pops a value from the stack as a sprite handle.
    /// </summary>
    /// <returns>The handle, or 0 if there is no handle. A nonzero number that isn't a sprite handle is logged.</returns>
    [EngineRoutine(0x00439fd0)]
    public int PopHandle()
    {
        ref readonly var slot = ref _stack[^1];
        if (slot.Value != 0 && !slot.Flags.HasFlag(ScriptStackFlags.Sprite))
        {
            Error?.Error("LOGIC"u8, LogError.Text, 0, "this variable is not unit"u8);
        }

        return _stack.Pop().GetValue();
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

    [EngineRoutine(0x004231c0)]
    internal ReadOnlyMemory<byte> GetVariableString(ReadOnlyMemory<byte> reference)
    {
        int bracket = reference.Span.IndexOf(VariableElementStart);
        int index = _objects.Location(bracket < 0 ? reference.Span : reference.Span[..bracket]);
        if (index < 0)
        {
            Error?.Log($"!!!ERROR!!! SCRIPT Can't find variable '{MyError.Decode(reference.Span)}' in GetVariableString");
            return default;
        }

        int element = reference.After("["u8).Span.ParseNumber();
        ref var slot = ref GetSlot(_objects[index].Address + element);
        if (slot.Flags.HasFlag(ScriptStackFlags.Number))
        {
            slot.Text = ScriptStack.FormatNumber(slot.Value);
        }

        return slot.Text;
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

    private void RestoreStack(int count)
    {
        _stack.Expand(count);
        _stack.Count = count;
    }
}
