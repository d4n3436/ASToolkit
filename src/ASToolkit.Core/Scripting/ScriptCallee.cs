using ASToolkit.Core.Engine.Scripts;

namespace ASToolkit.Core.Scripting;

internal readonly record struct ScriptCallee(int Id, int CallOperand, int FirstVariable, int Count)
{
    public static ScriptCallee None { get; } = new(-1, -1, 0, 0);

    public static ScriptCallee Find(Script script, ReadOnlySpan<byte> name)
    {
        int found = script.FindObject(name);
        if (found < 0)
            return None;

        var entry = script.Objects[found];

        return entry.Kind switch
        {
            ScriptObjectKind.Extern => new ScriptCallee(entry.Address, -1, entry.FirstVariable, entry.Count),
            ScriptObjectKind.Function => new ScriptCallee(-1, script.GetCallOperand(found), entry.FirstVariable, entry.Count),
            _ => None
        };
    }

    public int SkipDefaults(ReadOnlySpan<ScriptInstruction> code, int index)
    {
        while ((uint)index < (uint)code.Length && code[index].Opcode == ScriptOpcode.Load && code[index].Operand >= FirstVariable && code[index].Operand < FirstVariable + Count)
        {
            index++;
        }

        return index;
    }

    public bool IsExternAt(ReadOnlySpan<ScriptInstruction> code, int index) =>
        Id >= 0 && (uint)index < (uint)code.Length && code[index].IsExternCall && (int)code[index].Opcode == Id;

    public bool IsExternCallAt(ReadOnlySpan<ScriptInstruction> code, int index) => IsExternAt(code, SkipDefaults(code, index));

    public bool IsFunctionCallAt(ReadOnlySpan<ScriptInstruction> code, int index) =>
        CallOperand >= 0 && (uint)index < (uint)code.Length && code[index].Opcode == ScriptOpcode.Call && code[index].Operand == CallOperand;

    public bool IsCallAt(ReadOnlySpan<ScriptInstruction> code, int index) => IsFunctionCallAt(code, index) || IsExternAt(code, index);

    public bool IsCalledIn(ReadOnlySpan<ScriptInstruction> code)
    {
        for (int index = 0; index < code.Length; index++)
        {
            if (IsCallAt(code, index))
                return true;
        }

        return false;
    }
}
