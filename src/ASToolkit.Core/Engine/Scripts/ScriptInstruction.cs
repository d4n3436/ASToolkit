using System.Buffers.Binary;

namespace ASToolkit.Core.Engine.Scripts;

/// <summary>
/// Represents one instruction of a compiled script's code.
/// </summary>
/// <param name="Offset">The offset of the instruction in the code.</param>
/// <param name="Opcode">The opcode of the instruction, or the id of the built-in function it calls.</param>
/// <param name="Operand">The operand of the instruction, or 0 when it has none.</param>
/// <param name="Length">The length of the instruction in bytes.</param>
public readonly record struct ScriptInstruction(int Offset, ScriptOpcode Opcode, int Operand, int Length)
{
    /// <summary>
    /// Gets a value indicating whether the instruction calls a built-in function.
    /// </summary>
    public bool IsExternCall => Opcode is not (>= ScriptOpcode.PushNumber and <= ScriptOpcode.Pop or >= ScriptOpcode.Jump and <= ScriptOpcode.SetIndex);

    /// <summary>
    /// Gets the offset of the instruction that follows.
    /// </summary>
    public int End => Offset + Length;

    /// <summary>
    /// Gets the offset a jump instruction leads to.
    /// </summary>
    public int Target => Opcode == ScriptOpcode.Call ? Operand : Offset + 1 + Operand;

    /// <summary>
    /// Reads the instruction at an offset of compiled code.
    /// </summary>
    /// <param name="code">The compiled code.</param>
    /// <param name="offset">The offset of the instruction.</param>
    /// <returns>The instruction.</returns>
    public static ScriptInstruction Read(ReadOnlySpan<byte> code, int offset)
    {
        var opcode = (ScriptOpcode)code[offset];
        switch (opcode)
        {
            case ScriptOpcode.PushText:
                return new ScriptInstruction(offset, opcode, 0, code[(offset + 1)..].IndexOf((byte)0) + 2);
            case ScriptOpcode.PushNumber:
            case ScriptOpcode.JumpIfFalse:
            case ScriptOpcode.EndStatement:
            case ScriptOpcode.Jump:
            case ScriptOpcode.JumpIfFalseOnce:
            case ScriptOpcode.Call:
            case >= ScriptOpcode.PostIncrement and <= ScriptOpcode.Assign:
                return new ScriptInstruction(offset, opcode, BinaryPrimitives.ReadInt32LittleEndian(code[(offset + 1)..]), 5);
            case ScriptOpcode.CompoundAssign:
                return new ScriptInstruction(offset, opcode, BinaryPrimitives.ReadInt32LittleEndian(code[(offset + 1)..]), 6);
            default:
                return new ScriptInstruction(offset, opcode, 0, 1);
        }
    }
}
