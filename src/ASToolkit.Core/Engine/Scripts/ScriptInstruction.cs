using System.Buffers.Binary;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine.Scripts;

/// <summary>
/// Represents one instruction of a compiled script's code.
/// </summary>
/// <param name="Offset">The offset of the instruction in the code.</param>
/// <param name="Opcode">The opcode of the instruction, or the id of the built-in function it calls.</param>
/// <param name="Operand">The operand of the instruction, or 0 if the instruction doesn't have one.</param>
/// <param name="Length">The length of the instruction in bytes.</param>
/// <param name="IsExternCall">A value indicating whether the instruction calls a built-in function.</param>
public readonly record struct ScriptInstruction(int Offset, ScriptOpcode Opcode, int Operand, int Length, bool IsExternCall)
{
    /// <summary>
    /// Gets the offset of the instruction that follows.
    /// </summary>
    public int End => Offset + Length;

    /// <summary>
    /// Gets the offset a jump instruction leads to.
    /// </summary>
    public int Target => Offset + 1 + Operand;

    /// <summary>
    /// Reads the instruction at an offset of compiled code.
    /// </summary>
    /// <param name="code">The compiled code.</param>
    /// <param name="offset">The offset of the instruction.</param>
    /// <param name="version">The version of the script language the code was compiled in.</param>
    /// <returns>The instruction.</returns>
    public static ScriptInstruction Read(ReadOnlySpan<byte> code, int offset, ScriptVersion version)
    {
        var opcode = (ScriptOpcode)code[offset];
        if (opcode is not (>= ScriptOpcode.PushNumber and <= ScriptOpcode.Pop or >= ScriptOpcode.Jump and <= ScriptOpcode.SetIndex) &&
            (version != ScriptVersion.AlienShooter2 || opcode is not (>= ScriptOpcode.CastString and <= ScriptOpcode.OrJump)))
            return new ScriptInstruction(offset, opcode, 0, 1, true);

        return opcode switch
        {
            ScriptOpcode.PushText => new ScriptInstruction(offset, opcode, 0, code[(offset + 1)..].IndexOf((byte)0) + 2, false),
            ScriptOpcode.PushNumber or ScriptOpcode.JumpIfFalse or ScriptOpcode.EndStatement or ScriptOpcode.Jump or ScriptOpcode.JumpIfFalseOnce
                or ScriptOpcode.Call or >= ScriptOpcode.PostIncrement and <= ScriptOpcode.Assign or ScriptOpcode.AndJump
                or ScriptOpcode.OrJump => new ScriptInstruction(offset, opcode, BinaryPrimitives.ReadInt32LittleEndian(code[(offset + 1)..]), 5, false),
            ScriptOpcode.CompoundAssign => new ScriptInstruction(offset, opcode, BinaryPrimitives.ReadInt32LittleEndian(code[(offset + 1)..]), 6, false),
            _ => new ScriptInstruction(offset, opcode, 0, 1, false)
        };
    }
}
