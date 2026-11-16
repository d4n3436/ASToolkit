using System.Buffers.Binary;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine.Scripts;

/// <summary>
/// Represents a script of the engine, compiled with its variables, names and code, and the stack it runs on.
/// </summary>
public sealed partial class Script
{
    private const int MinCapacity = 0x80;
    private StackObject[] _slots = [];
    private int _count;
    private ScriptObject[] _objects = [];
    private int _objectCount;
    private byte[]? _code;

    /// <summary>
    /// Creates an empty script in Alien Shooter's version of the script language.
    /// </summary>
    public Script() : this(ScriptVersion.AlienShooter)
    {
    }

    /// <summary>
    /// Creates an empty script in a version of the script language.
    /// </summary>
    /// <param name="version">The version of the script language the script is compiled in.</param>
    public Script(ScriptVersion version)
    {
        Version = version;
        _defineLookup = _defines.GetAlternateLookup<ReadOnlySpan<byte>>();
    }

    /// <summary>
    /// Gets the version of the script language the script is compiled in.
    /// </summary>
    public ScriptVersion Version { get; }

    private bool IsAlienShooter2 => Version == ScriptVersion.AlienShooter2;

    /// <summary>
    /// Gets the index of the object named <c>main</c>, or -1 when the script has none.
    /// </summary>
    public int MainIndex { get; private set; } = -1;

    /// <summary>
    /// Gets the number of variable slots, or the stack height while the script runs.
    /// </summary>
    public int VariableCount => _count;

    /// <summary>
    /// Gets the names of the script.
    /// </summary>
    public ReadOnlySpan<ScriptObject> Objects => _objects.AsSpan(0, _objectCount);

    /// <summary>
    /// Gets the compiled code.
    /// </summary>
    public ReadOnlySpan<byte> Code => _code;

    /// <summary>
    /// Returns a variable or stack slot.
    /// </summary>
    /// <param name="index">The index of the slot.</param>
    /// <returns>The slot.</returns>
    public ref StackObject GetSlot(int index) => ref _slots[index];

    /// <summary>
    /// Returns the index of the last object with a name, or -1 when there is none.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The index of the object.</returns>
    public int FindObject(ReadOnlySpan<byte> name)
    {
        for (int i = _objectCount - 1; i >= 0; i--)
        {
            if (_objects[i].Name.Span.SequenceEqual(name))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Returns the code offset where a function's code ends, at the next function's start.
    /// </summary>
    /// <param name="index">The index of the function's object.</param>
    /// <returns>The end offset.</returns>
    public int GetFunctionEnd(int index)
    {
        int start = _objects[index].Address;
        int end = _code?.Length ?? 0;
        for (int other = 0; other < _objectCount; other++)
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
    /// Returns the index of the function a call instruction's operand names, or -1 when there is none.
    /// </summary>
    /// <param name="operand">The operand of the call instruction.</param>
    /// <returns>The index of the function's object.</returns>
    public int FindCalledFunction(int operand)
    {
        if (IsAlienShooter2)
            return (uint)operand < (uint)_objectCount && _objects[operand].Kind == ScriptObjectKind.Function ? operand : -1;

        for (int index = 0; index < _objectCount; index++)
        {
            if (_objects[index].Kind == ScriptObjectKind.Function && _objects[index].Address == operand)
                return index;
        }

        return -1;
    }

    /// <summary>
    /// Returns the address of a slot's text as a number, never 0.
    /// </summary>
    /// <param name="slot">The index of the slot.</param>
    /// <returns>The address.</returns>
    public static int GetTextAddress(int slot) => slot + 1;

    /// <summary>
    /// Pushes a value onto the stack.
    /// </summary>
    /// <param name="slot">The value.</param>
    [EngineRoutine(0x00424e60)]
    public void Push(StackObject slot)
    {
        if (_count >= _slots.Length)
        {
            Array.Resize(ref _slots, _slots.Length * 2 + 4);
        }

        _slots[_count++] = slot;
    }

    /// <summary>
    /// Pops a value from the stack.
    /// </summary>
    /// <returns>The value.</returns>
    public StackObject Pop() => _slots[--_count];

    /// <summary>
    /// Pops a value from the stack as a number.
    /// </summary>
    /// <returns>The number.</returns>
    [EngineRoutine(0x00439fd0)]
    public int PopNumber() => _slots[--_count].GetValue();

    /// <summary>
    /// Pops a value from the stack as a text, writing a number in decimal.
    /// </summary>
    /// <returns>The text.</returns>
    [EngineRoutine(0x0043a390)]
    public ReadOnlyMemory<byte> PopText()
    {
        var value = _slots[--_count];

        return value.Flags.HasFlag(StackObjectFlags.Number) ? StackObject.FormatNumber(value.Value) : value.Text;
    }

    /// <summary>
    /// Pushes a text onto the stack.
    /// </summary>
    /// <param name="text">The text.</param>
    [EngineRoutine(0x0043a640)]
    public void PushText(ReadOnlyMemory<byte> text) => Push(StackObject.FromText(text));

    /// <summary>
    /// Clears every variable holding the handle of a deleted sprite.
    /// </summary>
    /// <param name="handle">The handle of the sprite.</param>
    [EngineRoutine(0x00424460)]
    public void ClearSpriteHandle(int handle)
    {
        for (int i = 0; i < _count; i++)
        {
            ref var slot = ref _slots[i];
            if (slot.Flags.HasFlag(StackObjectFlags.Sprite) && slot.Value == handle)
            {
                slot.Value = 0;
                slot.Flags &= ~StackObjectFlags.Sprite;
            }
        }
    }

    /// <summary>
    /// Pushes a number onto the stack.
    /// </summary>
    /// <param name="value">The number.</param>
    [EngineRoutine(0x0043a060)]
    public void PushNumber(int value) => Push(StackObject.FromNumber(value));

    /// <summary>
    /// Pushes a sprite handle onto the stack.
    /// </summary>
    /// <param name="value">The handle, or 0 for none.</param>
    [EngineRoutine(0x0043a0f0)]
    public void PushSprite(int value) =>
        Push(new StackObject(value == 0 ? StackObjectFlags.Number : StackObjectFlags.Number | StackObjectFlags.Sprite, value, default));

    /// <summary>
    /// Runs a function of the script.
    /// </summary>
    /// <param name="host">The engine running the built-in functions.</param>
    /// <param name="function">The index of the function, or a negative index for <c>main</c>.</param>
    /// <param name="first">The first argument, a sprite handle when nonzero.</param>
    /// <param name="second">The second argument, a sprite handle when nonzero.</param>
    /// <param name="third">The third argument.</param>
    /// <returns>1 when the function destroys the sprite of its first argument, else the value of the outermost <c>return</c>, else 0.</returns>
    /// <exception cref="NotSupportedException">The script is an Alien Shooter 2 script.</exception>
    [EngineRoutine(0x00423430)]
    public int CallFunction(IScriptFunctionHost host, int function, int first, int second, int third)
    {
        if (IsAlienShooter2)
            throw new NotSupportedException("Cannot run Alien Shooter 2 scripts.");

        int result = 0;
        bool indexPending = false;
        int index = 0;
        if (_code is null)
            return 0;

        if (function < 0)
        {
            function = MainIndex;
        }

        if (function >= _objectCount || function < 0)
            return 0;

        ref readonly var entry = ref _objects[function];
        if (entry.Kind != ScriptObjectKind.Function)
            return 0;

        var code = _code;
        int codeSize = code.Length;
        int savedCount = _count;
        PushNumber(savedCount);
        PushNumber(codeSize);
        int frame = _count;
        SetArgument(entry, 0, first, true);
        SetArgument(entry, 1, second, true);
        SetArgument(entry, 2, third, false);
        int pc = entry.Address;
        int statement = pc;
        while (pc < codeSize)
        {
            if (_count < frame)
                throw new InvalidOperationException($"Cannot pop below the frame of script function {function} at {pc}.");

            var op = (ScriptOpcode)code[pc];
            if (op is >= ScriptOpcode.Divide and <= ScriptOpcode.ShiftLeft)
            {
                pc++;
                BinarOperator(ref _slots[_count - 2], op, _slots[_count - 1]);
                _count--;
                continue;
            }

            if (op is >= ScriptOpcode.PostIncrement and <= ScriptOpcode.CompoundAssign)
            {
                int operand = ReadInt(code, pc + 1);
                int target = operand;
                if (_slots[operand].Flags.HasFlag(StackObjectFlags.Pointer) && indexPending)
                {
                    target = _slots[operand].Value;
                }

                target += index;
                pc += RunVariableOp(op, operand, target, indexPending, index, code, pc);
                indexPending = false;
                index = 0;
                continue;
            }

            switch (op)
            {
                case ScriptOpcode.PushNumber:
                    PushNumber(ReadInt(code, pc + 1));
                    pc += 5;
                    break;
                case ScriptOpcode.PushText:
                    int length = code.AsSpan(pc + 1).IndexOf((byte)0);
                    Push(StackObject.FromText(code.AsMemory(pc + 1, length)));
                    pc += length + 2;
                    break;
                case ScriptOpcode.Negate:
                    SetTop(-_slots[_count - 1].GetValue());
                    pc++;
                    break;
                case ScriptOpcode.BitwiseNot:
                    SetTop(~_slots[_count - 1].GetValue());
                    pc++;
                    break;
                case ScriptOpcode.LogicalNot:
                    SetTop(_slots[_count - 1].GetValue() == 0 ? 1 : 0);
                    pc++;
                    break;
                case ScriptOpcode.JumpIfFalse:
                    pc = _slots[--_count].IsTrue() ? pc + 5 : pc + 1 + ReadInt(code, pc + 1);
                    _count = frame;
                    statement = pc;
                    break;
                case ScriptOpcode.EndStatement:
                    pc += 5;
                    _count = frame;
                    statement = pc;
                    break;
                case ScriptOpcode.Pop:
                    _count--;
                    pc++;
                    break;
                case ScriptOpcode.Jump:
                    pc += 1 + ReadInt(code, pc + 1);
                    break;
                case ScriptOpcode.JumpIfFalseOnce:
                    int skip = pc + 1 + ReadInt(code, pc + 1);
                    if (!_slots[--_count].IsTrue())
                    {
                        pc = skip;
                    }
                    else
                    {
                        code[statement] = (byte)ScriptOpcode.Jump;
                        BinaryPrimitives.WriteInt32LittleEndian(code.AsSpan(statement + 1), skip - (statement + 1));
                        pc += 5;
                    }

                    _count = frame;
                    statement = pc;
                    break;
                case ScriptOpcode.Call:
                    PushNumber(frame);
                    PushNumber(pc + 5);
                    pc = ReadInt(code, pc + 1);
                    frame = _count;
                    statement = pc;
                    break;
                case ScriptOpcode.Return:
                    if (_count > frame)
                    {
                        var value = _slots[--_count];
                        _count = frame;
                        pc = _slots[--_count].GetValue();
                        frame = _slots[--_count].GetValue();
                        statement = pc;
                        Push(value);
                        if (pc >= codeSize && result == 0)
                        {
                            result = value.GetValue();
                        }
                    }
                    else
                    {
                        _count = frame;
                        pc = _slots[--_count].GetValue();
                        frame = _slots[--_count].GetValue();
                        statement = pc;
                    }

                    break;
                case ScriptOpcode.SetIndex:
                    indexPending = true;
                    index = _slots[--_count].GetValue();
                    pc++;
                    break;
                default:
                    if ((ScriptFunction)op == ScriptFunction.Destroy && _slots[_count - 1].GetValue() == first)
                    {
                        result = 1;
                    }

                    pc++;
                    host.ExecFunc(this, (int)op);
                    break;
            }
        }

        _count = savedCount;

        return result;
    }

    private void SetArgument(in ScriptObject entry, int parameter, int value, bool handle)
    {
        if (entry.Count <= parameter)
            return;

        var flags = handle && value != 0 ? StackObjectFlags.Number | StackObjectFlags.Sprite : StackObjectFlags.Number;
        _slots[entry.FirstVariable + parameter] = new StackObject(flags, value, default);
    }

    private void SetTop(int value)
    {
        ref var top = ref _slots[_count - 1];
        top.Flags = StackObjectFlags.Number;
        top.Value = value;
    }

    private int RunVariableOp(ScriptOpcode op, int operand, int target, bool indexPending, int index, ReadOnlySpan<byte> code, int pc)
    {
        switch (op)
        {
            case ScriptOpcode.PostIncrement:
            case ScriptOpcode.PostDecrement:
                Push(_slots[target]);
                Step(ref _slots[target], op == ScriptOpcode.PostIncrement ? 1 : -1);
                break;
            case ScriptOpcode.PreIncrement:
            case ScriptOpcode.PreDecrement:
                Step(ref _slots[target], op == ScriptOpcode.PreIncrement ? 1 : -1);
                Push(_slots[target]);
                break;
            case ScriptOpcode.Load:
                if (!indexPending && _slots[target].Flags.HasFlag(StackObjectFlags.ArrayElement))
                {
                    PushNumber(operand);
                }
                else
                {
                    Push(_slots[target]);
                }

                break;
            case ScriptOpcode.LoadTextAddress:
                PushNumber(_slots[target].Flags.HasFlag(StackObjectFlags.Text) ? GetTextAddress(target) : 0);
                break;
            case ScriptOpcode.Assign:
                Assign(operand, target, indexPending, index);
                break;
            case ScriptOpcode.CompoundAssign:
                var value = _slots[--_count];
                BinarOperator(ref _slots[target], (ScriptOpcode)code[pc + 5], value);
                Push(_slots[target]);
                return 6;
        }

        return 5;
    }

    private static void Step(ref StackObject slot, int amount)
    {
        slot.Flags &= ~StackObjectFlags.Sprite;
        if ((slot.Flags & (StackObjectFlags.Number | StackObjectFlags.Pointer)) != 0)
        {
            slot.Value = unchecked(slot.Value + amount);
        }
    }

    private void Assign(int operand, int target, bool indexPending, int index)
    {
        var top = _slots[_count - 1];
        if (!_slots[operand].Flags.HasFlag(StackObjectFlags.Text))
        {
            ref var destination = ref _slots[target];
            destination.Flags &= ~(StackObjectFlags.Sprite | StackObjectFlags.NotInitialized);
            destination.Value = top.GetValue();
            if (top.Flags.HasFlag(StackObjectFlags.Sprite))
            {
                destination.Flags |= StackObjectFlags.Sprite;
            }
        }
        else if (indexPending && !_slots[operand].Flags.HasFlag(StackObjectFlags.ArrayElement))
        {
            StoreCharacter(ref _slots[operand], index, (byte)top.GetValue());
        }
        else
        {
            _slots[target].Text = top.Flags.HasFlag(StackObjectFlags.Number) ? StackObject.FormatNumber(top.Value) : top.Text.ToArray();
        }
    }

    private static void StoreCharacter(ref StackObject slot, int index, byte value)
    {
        if ((uint)index >= (uint)slot.Text.Length)
            return;

        if (value == 0)
        {
            slot.Text = slot.Text[..index];
            return;
        }

        byte[] text = slot.Text.ToArray();
        text[index] = value;
        slot.Text = text;
    }

    [EngineRoutine(0x004245e0)]
    private static void BinarOperator(ref StackObject left, ScriptOpcode op, in StackObject right)
    {
        int value = right.GetValue();
        bool texts = left.Flags.HasFlag(StackObjectFlags.Text) && right.Flags.HasFlag(StackObjectFlags.Text);
        if (left.Flags.HasFlag(StackObjectFlags.Text))
        {
            left.Value = StackObject.ParseNumber(left.Text.Span);
        }

        int current = left.Value;
        switch (op)
        {
            case ScriptOpcode.Divide:
                left.Value = value == 0 ? 0x0fffffff : current / value;
                break;
            case ScriptOpcode.Modulo:
                left.Value = current % value;
                break;
            case ScriptOpcode.Add when texts:
                byte[] joined = GC.AllocateUninitializedArray<byte>(left.Text.Length + right.Text.Length);
                left.Text.Span.CopyTo(joined);
                right.Text.Span.CopyTo(joined.AsSpan(left.Text.Length));
                left.Text = joined;
                left.Flags = StackObjectFlags.Text;
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
                left.Value = (texts ? left.Text.Span.SequenceEqual(right.Text.Span) : current == value) ? 1 : 0;
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
                left.Value = (texts ? !left.Text.Span.SequenceEqual(right.Text.Span) : current != value) ? 1 : 0;
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
        }

        left.Flags = StackObjectFlags.Number;
    }

    private static int ReadInt(ReadOnlySpan<byte> code, int offset) => BinaryPrimitives.ReadInt32LittleEndian(code[offset..]);
}
