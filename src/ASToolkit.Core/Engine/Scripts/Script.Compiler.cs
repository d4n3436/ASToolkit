using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine.Scripts;

public sealed partial class Script
{
    private const int TextPrefix = 0xfe2;
    private const int TextExtra = 0x1000;
    private const int CodeCapacity = 0x3e800;
    private const int MaxNameLength = 0xfff;
    private const int MaxBreaks = 0x80;
    private static readonly string[] ErrorFormats =
    [
        "0x{0:X} Couldn't lock {1}", "0x{0:X} Couldn't copy {1}", "{0} There was not enough memory for {1}", "0x{0:X} Couldn't create the {1}",
        "0x{0:X} Invalid {1}", "0x{0:X} Load {1}", "0x{0:X} Save {1}", "0x{0:X} Couldn't open '{1}'", "0x{0:X} Couldn't set the {1}",
        "0x{0:X} Couldn't get the {1}", "{0} {1}", "0x{0:X} Section can't found ({1})", "0x{0:X} Unable initialize {1}", "{0} Missing {1}",
        "{0} Unknownn {1}"
    ];

    private readonly Dictionary<byte[], byte[]> _defines = new(ScriptNameComparer.Instance);
    private readonly Dictionary<byte[], byte[]>.AlternateLookup<ReadOnlySpan<byte>> _defineLookup;
    private IScriptFileSource? _source;
    private string _fileName = "";
    private byte[] _text = [];
    private int _position;
    private int _end;
    private int _line;
    private int _ifDepth;
    private bool _noSubstitution;
    private byte[] _codeBuffer = [];
    private int _codeSize;
    private readonly byte[] _name = new byte[MaxNameLength + 1];
    private int _nameLength;

    /// <summary>
    /// Gets the path of the script file.
    /// </summary>
    public string FileName => _fileName;

    /// <summary>
    /// Loads a script file, compiling its source or reading its compiled form.
    /// </summary>
    /// <param name="source">The files the script and its includes are opened from.</param>
    /// <param name="path">The path of the script file.</param>
    /// <returns><see langword="true"/> when the file was opened; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="InvalidDataException">The source has an error the engine stops on.</exception>
    [EngineRoutine(0x0041f7d0)]
    public bool Load(IScriptFileSource source, string path)
    {
        Reset();
        _fileName = path;
        if (!source.TryRead(path, out var file))
            return false;

        Span<byte> head = stackalloc byte[sizeof(int)];
        file.Span[..Math.Min(file.Length, head.Length)].CopyTo(head);
        if (head[^1] != 0)
        {
            Compile(source, path, file.Span);
            return true;
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(head);
        EnsureVariableCapacity(Math.Max(count, MinCapacity));
        _count = count;
        int offset = sizeof(uint) + ReadState(file[sizeof(uint)..]);
        int size = BinaryPrimitives.ReadInt32LittleEndian(file.Span[offset..]);
        offset += sizeof(int);
        _code = file.Slice(offset, size).ToArray();
        for (int i = 0; i < _objectCount; i++)
        {
            if (_objects[i].Name.Span.SequenceEqual("main"u8))
            {
                MainIndex = i;
            }
        }

        return true;
    }

    [EngineRoutine(0x0041f450)]
    private void Compile(IScriptFileSource source, string path, ReadOnlySpan<byte> file)
    {
        _fileName = path;
        _source = source;
        _codeBuffer = ArrayPool<byte>.Shared.Rent(CodeCapacity);
        _codeSize = 0;
        EnsureVariableCapacity(MinCapacity);
        EnsureObjectCapacity(MinCapacity);
        _line = 0;
        try
        {
            OpenText(file);
            try
            {
                while (!ParseTopLevelItem())
                {
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(_text);
                _text = [];
            }

            _defines.Clear();
            if (_codeSize == 0)
            {
                Reset();
            }
            else
            {
                _code = _codeBuffer.AsSpan(0, _codeSize).ToArray();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(_codeBuffer);
            _codeBuffer = [];
            _source = null;
        }
    }

    [EngineRoutine(0x0041f330)]
    private void Reset()
    {
        _code = null;
        Array.Clear(_slots);
        Array.Clear(_objects);
        _count = 0;
        _objectCount = 0;
        _defines.Clear();
        _codeSize = 0;
        _ifDepth = 0;
        _noSubstitution = false;
        MainIndex = -1;
    }

    private void OpenText(ReadOnlySpan<byte> file)
    {
        _text = ArrayPool<byte>.Shared.Rent(file.Length + TextExtra);
        _text.AsSpan(0, file.Length + TextExtra).Clear();
        file.CopyTo(_text.AsSpan(TextPrefix));
        _position = TextPrefix;
        _end = TextPrefix + file.Length;
    }

    private void EnsureVariableCapacity(int count)
    {
        if (_slots.Length < count)
        {
            Array.Resize(ref _slots, count);
        }
    }

    private byte At(int index) => (uint)index < (uint)_text.Length ? _text[index] : (byte)0;

    private static bool IsAlpha(byte character) => char.IsAsciiLetter((char)character);

    private static bool IsAlphanumeric(byte character) => char.IsAsciiLetterOrDigit((char)character);

    private static bool IsNameCharacter(byte character) => IsAlphanumeric(character) || character == '_';

    private static bool IsSpace(byte character) => character == ' ' || character is >= 0x09 and <= 0x0d;

    [EngineRoutine(0x0041fbe0)]
    private InvalidDataException ReportError(int code, string text, int extra = 0) =>
        new($"'{_fileName}' line {_line + 1}: {string.Format(CultureInfo.InvariantCulture, ErrorFormats[code], extra, text)}");

    private static string Decode(ReadOnlySpan<byte> text) => GameTextEncoding.Cyrillic.GetString(text);

    [EngineRoutine(0x0041fcb0)]
    private bool SkipBlanksAndDirectives()
    {
        int mode = 0;
        int skip = 0;
        if (_position < _end)
        {
            do
            {
                if (mode == 0)
                {
                    byte character = At(_position);
                    if ((IsAlpha(character) || character == '_') && !_noSubstitution)
                    {
                        Substitute();
                    }

                    int start = _position;
                    if (At(start) == '#')
                    {
                        skip = ReadConditional(skip);
                    }

                    character = At(_position);
                    if (character == '/' && At(_position + 1) == '/')
                    {
                        mode = 1;
                    }
                    else if (character == '/' && At(_position + 1) == '*')
                    {
                        mode = 2;
                    }
                    else
                    {
                        if (character == '?')
                            throw ReportError(10, "?: not supported in this version");

                        if (skip == 0 && !IsSpace(character) && character != 0)
                            return false;
                    }
                }
                else if (mode == 1)
                {
                    if (At(_position) == '\n')
                    {
                        mode = 0;
                    }
                }
                else if (At(_position) == '/' && At(_position - 1) == '*')
                {
                    mode = 0;
                }

                if (At(_position++) == '\n')
                {
                    _line++;
                }
            }
            while (_position < _end);
        }

        return true;
    }

    private void Substitute()
    {
        int length = 0;
        while (IsNameCharacter(At(_position + length)))
        {
            if (length >= MaxNameLength)
                throw ReportError(10, "Very long name");

            length++;
        }

        if (!_defineLookup.TryGetValue(_text.AsSpan(_position, length), out byte[]? found))
            return;

        var value = found.AsSpan();
        _position = _position + length - value.Length;
        value.CopyTo(_text.AsSpan(_position));
    }

    private int ReadConditional(int skip)
    {
        var text = _text.AsSpan(_position);
        if (text.StartsWith("#ifdef"u8) || text.StartsWith("#ifndef"u8))
        {
            bool negated = text.StartsWith("#ifndef"u8);
            _position += negated ? 7 : 6;
            _ifDepth++;
            if (skip == 0)
            {
                _noSubstitution = true;
                var name = ReadName();
                _noSubstitution = false;
                bool defined = FindObject(name) >= 0 || _defineLookup.ContainsKey(name);
                if (defined == negated)
                {
                    skip = _ifDepth;
                }
            }
        }
        else if (text.StartsWith("#endif"u8))
        {
            _position += 6;
            if (skip == _ifDepth)
            {
                skip = 0;
            }

            _ifDepth--;
        }
        else if (text.StartsWith("#else"u8))
        {
            _position += 5;
            if (skip != 0 || _ifDepth <= 0)
            {
                if (skip == _ifDepth)
                {
                    skip = 0;
                }
            }
            else
            {
                skip = _ifDepth;
            }
        }

        return skip;
    }

    [EngineRoutine(0x004202a0)]
    private void SkipOrFailAtEnd()
    {
        if (SkipBlanksAndDirectives())
            throw ReportError(10, "End of file");
    }

    [EngineRoutine(0x00420460)]
    private bool TryToken(ReadOnlySpan<byte> token)
    {
        SkipOrFailAtEnd();
        if (!_text.AsSpan(_position).StartsWith(token))
            return false;

        if ((IsAlpha(token[0]) || token[0] == '#') && IsNameCharacter(At(_position + token.Length)))
            return false;

        _position += token.Length;
        SkipBlanksAndDirectives();

        return true;
    }

    [EngineRoutine(0x004204f0)]
    private void ExpectToken(ReadOnlySpan<byte> token)
    {
        if (!TryToken(token))
            throw ReportError(13, Decode(token));
    }

    [EngineRoutine(0x004203b0)]
    private ReadOnlySpan<byte> ReadName()
    {
        SkipOrFailAtEnd();
        int length = 0;
        while (IsNameCharacter(At(_position)))
        {
            if (length >= MaxNameLength)
                throw ReportError(10, "Very long name");

            _name[length++] = _text[_position++];
        }

        if (length == 0)
            throw ReportError(4, "name");

        _nameLength = length;
        SkipOrFailAtEnd();

        return _name.AsSpan(0, _nameLength);
    }

    [EngineRoutine(0x004202d0)]
    private byte[] ReadDefineLine()
    {
        SkipOrFailAtEnd();
        int start = _position;
        while (At(_position) != '\n' && At(_position) != '\r')
        {
            if (_position - start >= MaxNameLength)
                throw ReportError(10, "Very long line");

            _position++;
        }

        var line = _text.AsSpan(start, _position - start);
        if (line.IsEmpty)
            throw ReportError(10, "empty line");

        int comment = line.IndexOf("//"u8);
        if (comment >= 0)
        {
            line = line[..comment];
        }

        byte[] value = line.TrimEnd(" \n\r\t"u8).ToArray();
        SkipOrFailAtEnd();

        return value;
    }

    [EngineRoutine(0x00420570)]
    private int ParseStringLiteral(Span<byte> output)
    {
        if (At(_position) != '"')
            return 0;

        int count = 0;
        byte character = At(++_position);
        while (character != '"' && _position < _end)
        {
            if (character == '\\')
            {
                byte next = At(_position + 1);
                if (next == '\r' && At(_position + 2) == '\n')
                {
                    _line++;
                    _position += 2;
                }
                else if (next == '\n')
                {
                    _line++;
                    _position++;
                }
                else if (char.IsAsciiDigit((char)next) && char.IsAsciiDigit((char)At(_position + 2)) && char.IsAsciiDigit((char)At(_position + 3)))
                {
                    output[count++] = (byte)(((next * 8 + At(_position + 2) + 10) & 0xff) * 8 + At(_position + 3));
                    _position += 3;
                }
                else
                {
                    output[count++] = next switch
                    {
                        (byte)'n' => (byte)'\n',
                        (byte)'r' => (byte)'\r',
                        (byte)'t' => (byte)'\t',
                        _ => next
                    };
                    _position++;
                }
            }
            else
            {
                output[count++] = character;
            }

            character = At(++_position);
        }

        if (_end <= _position++)
            throw ReportError(10, "End of file");

        output[count++] = 0;

        return count;
    }

    private void Emit(byte value) => _codeBuffer[_codeSize++] = value;

    private void Emit(ScriptOpcode op) => Emit((byte)op);

    private void EmitInt(int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(_codeBuffer.AsSpan(_codeSize), value);
        _codeSize += sizeof(int);
    }

    private void Emit(ScriptOpcode op, int operand)
    {
        Emit(op);
        EmitInt(operand);
    }

    private int ReserveInt()
    {
        int at = _codeSize;
        _codeSize += sizeof(int);

        return at;
    }

    private int ReadCode(int at) => BinaryPrimitives.ReadInt32LittleEndian(_codeBuffer.AsSpan(at));

    private void WriteCode(int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(_codeBuffer.AsSpan(at), value);

    private void PatchHere(int at) => WriteCode(at, _codeSize - at);

    private void EmitJumpBack(ScriptOpcode op, int target)
    {
        Emit(op);
        WriteCode(_codeSize, target - _codeSize);
        _codeSize += sizeof(int);
    }

    [EngineRoutine(0x00420520)]
    private int ParseConstInt()
    {
        SkipOrFailAtEnd();
        int start = _codeSize;
        ParseExpression();
        if (_codeBuffer[start] != (byte)ScriptOpcode.PushNumber || _codeSize - start != 5)
            throw ReportError(4, "constant int value");

        _codeSize -= 5;

        return ReadCode(start + 1);
    }

    [EngineRoutine(0x00421b70)]
    private void EmitBinaryOp(int start, ScriptOpcode op)
    {
        if (_codeBuffer[start] != (byte)ScriptOpcode.PushNumber || _codeBuffer[start + 5] != (byte)ScriptOpcode.PushNumber || _codeSize - start != 10)
        {
            Emit(op);
            return;
        }

        int left = ReadCode(start + 1);
        int right = ReadCode(start + 6);
        int result = op switch
        {
            ScriptOpcode.Divide => left / right,
            ScriptOpcode.Modulo => left % right,
            ScriptOpcode.Add => unchecked(left + right),
            ScriptOpcode.Subtract => unchecked(left - right),
            ScriptOpcode.Xor => left ^ right,
            ScriptOpcode.Or => left | right,
            ScriptOpcode.And => left & right,
            ScriptOpcode.Multiply => unchecked(right * left),
            ScriptOpcode.ShiftRight => left >> (right & 31),
            ScriptOpcode.ShiftLeft => left << (right & 31),
            _ => left
        };
        WriteCode(start + 1, result);
        _codeSize -= 5;
    }

    [EngineRoutine(0x00421f60)]
    private void ParseExpression()
    {
        int start = _codeSize;
        ParseComparison();
        while (true)
        {
            if (TryToken("^"u8))
            {
                ParseComparison();
                EmitBinaryOp(start, ScriptOpcode.Xor);
            }
            else if (TryToken("&&"u8))
            {
                ParseComparison();
                Emit(ScriptOpcode.LogicalAnd);
            }
            else if (TryToken("&"u8))
            {
                ParseComparison();
                EmitBinaryOp(start, ScriptOpcode.And);
            }
            else if (TryToken("||"u8))
            {
                ParseComparison();
                Emit(ScriptOpcode.LogicalOr);
            }
            else if (TryToken("|"u8))
            {
                ParseComparison();
                EmitBinaryOp(start, ScriptOpcode.Or);
            }
            else
                break;
        }
    }

    [EngineRoutine(0x00421e10)]
    private void ParseComparison()
    {
        int start = _codeSize;
        ParseAdditive();
        while (true)
        {
            if (TryToken(">="u8))
            {
                ParseAdditive();
                Emit(ScriptOpcode.GreaterOrEqual);
            }
            else if (TryToken(">>"u8))
            {
                ParseAdditive();
                EmitBinaryOp(start, ScriptOpcode.ShiftRight);
            }
            else if (TryToken(">"u8))
            {
                ParseAdditive();
                Emit(ScriptOpcode.Greater);
            }
            else if (TryToken("<="u8))
            {
                ParseAdditive();
                Emit(ScriptOpcode.LessOrEqual);
            }
            else if (TryToken("<<"u8))
            {
                ParseAdditive();
                EmitBinaryOp(start, ScriptOpcode.ShiftLeft);
            }
            else if (TryToken("<"u8))
            {
                ParseAdditive();
                Emit(ScriptOpcode.Less);
            }
            else if (TryToken("=="u8))
            {
                ParseAdditive();
                Emit(ScriptOpcode.Equal);
            }
            else if (TryToken("!="u8))
            {
                ParseAdditive();
                Emit(ScriptOpcode.NotEqual);
            }
            else
                break;
        }
    }

    [EngineRoutine(0x00421db0)]
    private void ParseAdditive()
    {
        int start = _codeSize;
        ParseMultiplicative();
        while (true)
        {
            if (TryToken("+"u8))
            {
                ParseMultiplicative();
                EmitBinaryOp(start, ScriptOpcode.Add);
            }
            else if (TryToken("-"u8))
            {
                ParseMultiplicative();
                EmitBinaryOp(start, ScriptOpcode.Subtract);
            }
            else
                break;
        }
    }

    [EngineRoutine(0x00421d30)]
    private void ParseMultiplicative()
    {
        int start = _codeSize;
        ParseTerm();
        while (true)
        {
            if (TryToken("*"u8))
            {
                ParseTerm();
                EmitBinaryOp(start, ScriptOpcode.Multiply);
            }
            else if (TryToken("/"u8))
            {
                ParseTerm();
                EmitBinaryOp(start, ScriptOpcode.Divide);
            }
            else if (TryToken("%"u8))
            {
                ParseTerm();
                EmitBinaryOp(start, ScriptOpcode.Modulo);
            }
            else
                break;
        }
    }

    [EngineRoutine(0x00420d50)]
    private void ParseTerm()
    {
        while (true)
        {
            var unary = ScriptOpcode.None;
            var access = ScriptOpcode.Load;
            if (At(_position + 1) == '-' || !TryToken("-"u8))
            {
                if (TryToken("~"u8))
                {
                    unary = ScriptOpcode.BitwiseNot;
                }
                else if (TryToken("!"u8))
                {
                    unary = ScriptOpcode.LogicalNot;
                }
            }
            else
            {
                unary = ScriptOpcode.Negate;
            }

            if (TryToken("--"u8))
            {
                access = ScriptOpcode.PreDecrement;
            }
            else if (TryToken("++"u8))
            {
                access = ScriptOpcode.PreIncrement;
            }
            else if (TryToken("&"u8))
            {
                access = ScriptOpcode.LoadTextAddress;
            }

            byte character = At(_position);
            if (char.IsAsciiDigit((char)character))
            {
                int value = StackObject.TryScanInteger(ReadName(), out int scanned) ? scanned : (int)access;
                value = unary switch
                {
                    ScriptOpcode.Negate => unchecked(-value),
                    ScriptOpcode.BitwiseNot => ~value,
                    ScriptOpcode.LogicalNot => value == 0 ? 1 : 0,
                    _ => value
                };
                Emit(ScriptOpcode.PushNumber, value);
                return;
            }

            if (character == '"')
            {
                Emit(ScriptOpcode.PushText);
                _codeSize += ParseStringLiteral(_codeBuffer.AsSpan(_codeSize));
            }
            else if (character == '\'')
            {
                Emit(ScriptOpcode.PushNumber);
                _position++;
                EmitInt((sbyte)At(_position++));
                if (At(_position) != '\'')
                    throw ReportError(13, "second '");

                _position++;
            }
            else if (TryToken("sizeof"u8))
            {
                ParseSize();
            }
            else if (TryToken("static"u8))
            {
                if (TryToken("int"u8))
                {
                    ParseIntDeclarators();
                }
                else if (TryToken("string"u8))
                {
                    ParseStringDeclarators();
                }
                else
                    throw ReportError(4, "static variable");
            }
            else if (TryToken("int"u8))
            {
                ParseIntDeclarators();
            }
            else if (TryToken("string"u8))
            {
                ParseStringDeclarators();
            }
            else if (TryToken("return"u8))
            {
                ParseExpression();
                Emit(ScriptOpcode.Return);
            }
            else if (TryToken("("u8))
            {
                ParseExpression();
                ExpectToken(")"u8);
            }
            else if (IsAlpha(At(_position)))
            {
                if (!ParseName(unary, access))
                    continue;

                return;
            }
            else
            {
                if (unary != ScriptOpcode.None || At(_position) == 0 || ".$#@`"u8.Contains(At(_position)))
                    throw ReportError(10, "error symbol");

                return;
            }

            EmitUnary(unary);
            return;
        }
    }

    private void EmitUnary(ScriptOpcode unary)
    {
        if (unary != ScriptOpcode.None)
        {
            Emit(unary);
        }
    }

    private void ParseSize()
    {
        if (!TryToken("("u8))
            throw ReportError(13, "'(' for sizeof");

        Emit(ScriptOpcode.PushNumber);
        if (TryToken("int"u8) || TryToken("string"u8))
        {
            EmitInt(sizeof(int));
        }
        else
        {
            int found = FindObject(ReadName());
            if (found < 0 || _objects[found].Kind != ScriptObjectKind.Variable)
                throw ReportError(4, "sizeof parameter");

            EmitInt(_objects[found].Count << 2);
        }

        ExpectToken(")"u8);
    }

    private bool ParseName(ScriptOpcode unary, ScriptOpcode access)
    {
        var name = ReadName();
        int found = FindObject(name);
        if (found < 0)
        {
            if (At(_position) != ':')
                throw ReportError(10, $"Undeclared identifier '{Decode(name)}'");

            byte[] label = name.ToArray();
            _position++;
            AddObject(new ScriptObject(label, ScriptObjectKind.Label, _codeSize));
            return false;
        }

        ref var target = ref _objects[found];
        switch (target.Kind)
        {
            case ScriptObjectKind.Variable:
                ParseVariable(found, access);
                break;
            case ScriptObjectKind.Extern:
                ParseExternCall(found);
                break;
            case ScriptObjectKind.Function:
                ParseFunctionCall(found);
                break;
            case ScriptObjectKind.Text:
                Emit(ScriptOpcode.PushText);
                target.Text.Span.CopyTo(_codeBuffer.AsSpan(_codeSize));
                _codeSize += target.Text.Length;
                Emit((byte)0);
                break;
            case ScriptObjectKind.Number:
                Emit(ScriptOpcode.PushNumber, target.Address);
                break;
            case ScriptObjectKind.Label:
                throw ReportError(10, At(_position) == ':' ? $"Label redefinition '{Decode(name)}'" : $"Incorrect use label '{Decode(name)}'");
            case ScriptObjectKind.PendingLabel:
                if (At(_position) != ':')
                    throw ReportError(10, $"Incorrect use label '{Decode(name)}'");

                _position++;
                target.Kind = ScriptObjectKind.Label;
                PatchHere(target.Address);
                target.Address = _codeSize;
                return false;
        }

        EmitUnary(unary);

        return true;
    }

    private void ParseVariable(int found, ScriptOpcode access)
    {
        int variable = _objects[found].Address;
        int indexLength = 0;
        byte[]? index = null;
        if (TryToken("["u8))
        {
            if ((_slots[variable].Flags & (StackObjectFlags.Text | StackObjectFlags.ArrayElement | StackObjectFlags.Pointer)) == 0)
                throw ReportError(10, "[] for not array");

            int indexStart = _codeSize;
            ParseExpression();
            Emit(ScriptOpcode.SetIndex);
            indexLength = _codeSize - indexStart;
            index = ArrayPool<byte>.Shared.Rent(indexLength);
            _codeBuffer.AsSpan(indexStart, indexLength).CopyTo(index);
            _codeSize = indexStart;
            ExpectToken("]"u8);
        }

        var op = access;
        var compound = ScriptOpcode.None;
        if (At(_position + 1) != '=' && TryToken("="u8))
        {
            ParseExpression();
            op = ScriptOpcode.Assign;
        }
        else if ((compound = TryCompoundToken()) != ScriptOpcode.None)
        {
            ParseExpression();
            op = ScriptOpcode.CompoundAssign;
        }
        else if (TryToken("++"u8))
        {
            op = ScriptOpcode.PostIncrement;
        }
        else if (TryToken("--"u8))
        {
            op = ScriptOpcode.PostDecrement;
        }

        if (index is not null)
        {
            index.AsSpan(0, indexLength).CopyTo(_codeBuffer.AsSpan(_codeSize));
            _codeSize += indexLength;
            ArrayPool<byte>.Shared.Return(index);
        }
        else if ((_slots[variable].Flags & (StackObjectFlags.ArrayElement | StackObjectFlags.Pointer)) != 0)
        {
            if (op is >= ScriptOpcode.PostIncrement and <= ScriptOpcode.PreDecrement)
                throw ReportError(10, "Increment or decrement for array");

            if (op != ScriptOpcode.Load)
                throw ReportError(4, "operation for array", (int)op);
        }

        Emit(op, variable);
        if (op == ScriptOpcode.CompoundAssign)
        {
            Emit(compound);
        }
    }

    private ScriptOpcode TryCompoundToken()
    {
        if (TryToken("+="u8))
            return ScriptOpcode.Add;
        if (TryToken("-="u8))
            return ScriptOpcode.Subtract;
        if (TryToken("/="u8))
            return ScriptOpcode.Divide;
        if (TryToken("*="u8))
            return ScriptOpcode.Multiply;
        if (TryToken("%="u8))
            return ScriptOpcode.Modulo;
        if (TryToken("&="u8))
            return ScriptOpcode.And;
        if (TryToken("|="u8))
            return ScriptOpcode.Or;
        if (TryToken("^="u8))
            return ScriptOpcode.Xor;
        if (TryToken("<<="u8))
            return ScriptOpcode.ShiftLeft;
        if (TryToken(">>="u8))
            return ScriptOpcode.ShiftRight;

        return ScriptOpcode.None;
    }

    private void ParseExternCall(int found)
    {
        var target = _objects[found];
        ExpectToken("("u8);
        int count = 0;
        if (!TryToken(")"u8))
        {
            do
            {
                ParseExpression();
                TryToken(","u8);
                count++;
            }
            while (!TryToken(")"u8));
        }

        for (; count < target.Count && _slots[target.FirstVariable + count].Flags.HasFlag(StackObjectFlags.Initialized); count++)
        {
            Emit(ScriptOpcode.Load, target.FirstVariable + count);
        }

        if (count != target.Count)
            throw ReportError(4, "extern function parameters number");

        Emit((byte)target.Address);
    }

    private void ParseFunctionCall(int found)
    {
        var target = _objects[found];
        ExpectToken("("u8);
        int count = 0;
        if (!TryToken(")"u8))
        {
            do
            {
                ParseExpression();
                TryToken(","u8);
                EmitArgumentStore(target.FirstVariable + count++);
            }
            while (!TryToken(")"u8));
        }

        for (; count < target.Count && _slots[target.FirstVariable + count].Flags.HasFlag(StackObjectFlags.Initialized); count++)
        {
            ref readonly var parameter = ref _slots[target.FirstVariable + count];
            if (!parameter.Flags.HasFlag(StackObjectFlags.Text))
            {
                Emit(ScriptOpcode.PushNumber, parameter.Value);
            }
            else
            {
                Emit(ScriptOpcode.PushText);
                parameter.Text.Span.CopyTo(_codeBuffer.AsSpan(_codeSize));
                _codeSize += parameter.Text.Length;
                Emit((byte)0);
            }

            EmitArgumentStore(target.FirstVariable + count);
        }

        if (count != target.Count)
            throw ReportError(4, "function parameters number");

        Emit(ScriptOpcode.Call, target.Address);
    }

    private void EmitArgumentStore(int variable)
    {
        Emit(ScriptOpcode.Assign, variable);
        Emit(ScriptOpcode.Pop);
    }

    private void ParseIntDeclarators()
    {
        do
        {
            ParseIntDeclarator();
        }
        while (TryToken(","u8));
    }

    private void ParseStringDeclarators()
    {
        do
        {
            ParseStringDeclarator();
        }
        while (TryToken(","u8));
    }

    [EngineRoutine(0x00420720)]
    private void ParseIntDeclarator()
    {
        var flags = StackObjectFlags.None;
        SkipOrFailAtEnd();
        if (At(_position) == '*')
        {
            flags = StackObjectFlags.Pointer;
            _position++;
        }

        var name = ReadName();
        if (FindObject(name) >= 0)
            throw ReportError(10, $"int redefinition '{Decode(name)}'");

        int symbol = AddObject(new ScriptObject(name.ToArray(), ScriptObjectKind.Variable, _count));
        int size;
        if (TryToken("["u8))
        {
            flags |= StackObjectFlags.ArrayElement;
            if (At(_position) == ']')
            {
                _position++;
                if (!TryToken("="u8))
                    throw ReportError(10, "for [] need initialisation");

                ExpectToken("{"u8);
                int count = 0;
                do
                {
                    Push(new StackObject(StackObjectFlags.Number | flags, 0, default));
                    ref var element = ref _slots[_count - 1];
                    int value = ParseConstInt();
                    element = ref _slots[_count - 1];
                    element.Value = value;
                    element.Flags |= StackObjectFlags.Initialized;
                    count++;
                }
                while (TryToken(","u8));

                ExpectToken("}"u8);
                _objects[symbol].Count = count;
                return;
            }

            size = ParseConstInt();
            ExpectToken("]"u8);
        }
        else
        {
            size = 1;
        }

        for (int element = 0; element < size; element++)
        {
            Push(new StackObject(StackObjectFlags.Number | flags | StackObjectFlags.NotInitialized, 0, default));
        }

        if (TryToken("="u8))
        {
            if (flags.HasFlag(StackObjectFlags.ArrayElement))
            {
                ParseArrayInitializer(size);
            }
            else
            {
                int value = ParseConstInt();
                ref var variable = ref _slots[_count - 1];
                variable.Value = value;
                variable.Flags = (variable.Flags | StackObjectFlags.Initialized) & ~StackObjectFlags.NotInitialized;
            }
        }

        _objects[symbol].Count = size;
    }

    private void ParseArrayInitializer(int size)
    {
        ExpectToken("{"u8);
        for (int element = 0; element < size; element++)
        {
            int value = ParseConstInt();
            ref var variable = ref _slots[_count - size + element];
            variable.Flags = (variable.Flags & ~StackObjectFlags.NotInitialized) | StackObjectFlags.Initialized;
            variable.Value = value;
            if (!TryToken(","u8))
            {
                ExpectToken("}"u8);
                return;
            }
        }

        throw ReportError(10, "too many initializers");
    }

    [EngineRoutine(0x00420aa0)]
    private void ParseStringDeclarator()
    {
        var name = ReadName();
        if (FindObject(name) >= 0)
            throw ReportError(10, $"string redifinition '{Decode(name)}'");

        byte[] symbolName = name.ToArray();
        var flags = StackObjectFlags.None;
        int size = 1;
        if (TryToken("["u8))
        {
            flags = StackObjectFlags.ArrayElement;
            size = ParseConstInt();
            ExpectToken("]"u8);
        }

        int symbol = AddObject(new ScriptObject(symbolName, ScriptObjectKind.Variable, _count));
        for (int element = 0; element < size; element++)
        {
            Push(new StackObject(StackObjectFlags.Text | flags, 0, default));
        }

        if (TryToken("="u8))
        {
            Span<byte> literal = stackalloc byte[MaxNameLength + 1];
            int length = ParseStringLiteral(literal);
            ref var variable = ref _slots[_count - 1];
            variable.Text = length > 0 ? literal[..(length - 1)].ToArray() : default;
            variable.Flags = (variable.Flags | StackObjectFlags.Initialized) & ~StackObjectFlags.NotInitialized;
        }

        _objects[symbol].Count = size;
    }

    [EngineRoutine(0x00424a20)]
    private int AddObject(ScriptObject entry)
    {
        if (_objectCount >= _objects.Length)
        {
            Array.Resize(ref _objects, _objects.Length * 2 + 4);
        }

        _objects[_objectCount] = entry;

        return _objectCount++;
    }

    private void TruncateObjects(int count)
    {
        if (count < _objectCount)
        {
            _objectCount = Math.Max(count, 0);
        }
    }

    [EngineRoutine(0x00422030)]
    private void ParseExpressionList()
    {
        do
        {
            ParseExpression();
            Emit(ScriptOpcode.EndStatement, _line);
        }
        while (TryToken(","u8));
    }

    [EngineRoutine(0x00422080)]
    private void ParseStatement(List<int>? breaks)
    {
        bool once = TryToken("iff"u8);
        if (once || TryToken("if"u8))
        {
            ExpectToken("("u8);
            ParseExpression();
            ExpectToken(")"u8);
            Emit(once ? ScriptOpcode.JumpIfFalseOnce : ScriptOpcode.JumpIfFalse);
            int skip = ReserveInt();
            ParseStatement(breaks);
            PatchHere(skip);
            if (TryToken("else"u8))
            {
                WriteCode(skip, ReadCode(skip) + 5);
                Emit(ScriptOpcode.Jump);
                int end = ReserveInt();
                ParseStatement(breaks);
                PatchHere(end);
            }
        }
        else if (TryToken("while"u8))
        {
            var loop = new List<int>();
            ExpectToken("("u8);
            int start = _codeSize;
            ParseExpression();
            ExpectToken(")"u8);
            Emit(ScriptOpcode.JumpIfFalse);
            int exit = ReserveInt();
            ParseStatement(loop);
            EmitJumpBack(ScriptOpcode.Jump, start);
            PatchHere(exit);
            PatchBreaks(loop);
        }
        else if (TryToken("do"u8))
        {
            var loop = new List<int>();
            int start = _codeSize;
            ParseStatement(loop);
            ExpectToken("while"u8);
            ExpectToken("("u8);
            ParseExpression();
            ExpectToken(")"u8);
            Emit(ScriptOpcode.LogicalNot);
            EmitJumpBack(ScriptOpcode.JumpIfFalse, start);
            PatchBreaks(loop);
        }
        else if (TryToken("for"u8))
        {
            var loop = new List<int>();
            ExpectToken("("u8);
            ParseExpressionList();
            ExpectToken(";"u8);
            int condition = _codeSize;
            ParseExpression();
            ExpectToken(";"u8);
            Emit(ScriptOpcode.JumpIfFalse);
            int exit = ReserveInt();
            Emit(ScriptOpcode.Jump);
            int body = ReserveInt();
            int step = _codeSize;
            ParseExpressionList();
            ExpectToken(")"u8);
            EmitJumpBack(ScriptOpcode.Jump, condition);
            PatchHere(body);
            ParseStatement(loop);
            EmitJumpBack(ScriptOpcode.Jump, step);
            PatchHere(exit);
            PatchBreaks(loop);
        }
        else if (TryToken("break"u8))
        {
            ExpectToken(";"u8);
            if (breaks is null)
                throw ReportError(10, "'break' without loop");

            if (breaks.Count >= MaxBreaks)
                throw ReportError(10, "Too many 'break'");

            Emit(ScriptOpcode.Jump);
            breaks.Add(ReserveInt());
        }
        else if (TryToken("goto"u8))
        {
            ParseGoto();
        }
        else if (TryToken("{"u8))
        {
            int saved = _objectCount;
            while (!TryToken("}"u8))
            {
                ParseStatement(breaks);
            }

            TruncateObjects(saved);
        }
        else
        {
            ParseExpressionList();
            ExpectToken(";"u8);
        }
    }

    private void PatchBreaks(List<int> breaks)
    {
        foreach (int at in breaks)
        {
            PatchHere(at);
        }
    }

    private void ParseGoto()
    {
        var name = ReadName();
        int found = FindObject(name);
        if (found < 0)
        {
            found = AddObject(new ScriptObject(name.ToArray(), ScriptObjectKind.PendingLabel, _codeSize + 1));
        }
        else if (_objects[found].Kind == ScriptObjectKind.PendingLabel)
            throw ReportError(10, $"second use undefined label '{Decode(name)}'");
        else if (_objects[found].Kind != ScriptObjectKind.Label)
            throw ReportError(10, $"'{Decode(name)}' is not label");

        EmitJumpBack(ScriptOpcode.Jump, _objects[found].Address);
    }

    [EngineRoutine(0x00422760)]
    private bool ParseTopLevelItem()
    {
        if (SkipBlanksAndDirectives())
            return true;

        var text = _text.AsSpan(_position);
        if (text.StartsWith("#define"u8))
        {
            _position += 7;
            _noSubstitution = true;
            byte[] name = ReadName().ToArray();
            _noSubstitution = false;
            _defines[name] = ReadDefineLine();

            return SkipBlanksAndDirectives();
        }

        if (text.StartsWith("#undef"u8))
        {
            _position += 6;
            _noSubstitution = true;
            bool found = _defineLookup.Remove(ReadName());
            _noSubstitution = false;
            if (!found)
                throw ReportError(4, "#undef parameters");

            return SkipBlanksAndDirectives();
        }

        if (TryToken("#include"u8))
        {
            ParseInclude();
            return SkipBlanksAndDirectives();
        }

        if (TryToken("extern"u8))
        {
            ParseExtern();
            return SkipBlanksAndDirectives();
        }

        if (TryToken("static"u8))
        {
            if (TryToken("int"u8))
            {
                ParseIntDeclarators();
            }
            else if (TryToken("string"u8))
            {
                ParseStringDeclarators();
            }
            else
                throw ReportError(4, "static variable");

            ExpectToken(";"u8);
            return SkipBlanksAndDirectives();
        }

        if (TryToken("int"u8))
        {
            ParseIntDeclarators();
            ExpectToken(";"u8);
            return SkipBlanksAndDirectives();
        }

        if (TryToken("string"u8))
        {
            ParseStringDeclarators();
            ExpectToken(";"u8);
            return SkipBlanksAndDirectives();
        }

        ParseFunction();

        return SkipBlanksAndDirectives();
    }

    private void ParseInclude()
    {
        string includer = _fileName;
        if (At(_position) != '"' && At(_position) != '<')
            throw ReportError(13, "include file name");

        int start = ++_position;
        while (At(_position) != '"' && At(_position) != '>')
        {
            if (_position >= _end)
                throw ReportError(10, "End of file");

            _position++;
        }

        string path = Decode(_text.AsSpan(start, _position++ - start));
        if (!_source!.TryRead(path, out var file))
            throw ReportError(7, path);

        var (text, position, end, line, depth) = (_text, _position, _end, _line, _ifDepth);
        OpenText(file.Span);
        _fileName = path;
        _line = 0;
        _ifDepth = 0;
        try
        {
            while (!ParseTopLevelItem())
            {
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(_text);
            (_text, _position, _end, _line, _ifDepth) = (text, position, end, line, depth);
            _fileName = includer;
        }
    }

    private void ParseExtern()
    {
        int saved = _objectCount;
        byte[] name = ReadName().ToArray();
        if (FindObject(name) >= 0)
            throw ReportError(10, "function redefinition");

        int firstVariable = ParseParameters();
        int id = ParseConstInt();
        if (!char.IsAsciiDigit((char)At(_position - 1)))
            throw ReportError(13, "extern function code");

        TruncateObjects(saved);
        AddObject(new ScriptObject(name, ScriptObjectKind.Extern, id, firstVariable, _count - firstVariable));
        ExpectToken(";"u8);
    }

    private int ParseParameters()
    {
        int firstVariable = _count;
        ExpectToken("("u8);
        do
        {
            if (TryToken("int"u8))
            {
                ParseIntDeclarator();
            }
            else if (TryToken("string"u8))
            {
                ParseStringDeclarator();
            }
        }
        while (TryToken(","u8));

        ExpectToken(")"u8);

        return firstVariable;
    }

    private void ParseFunction()
    {
        int saved = _objectCount;
        byte[] name = ReadName().ToArray();
        if (FindObject(name) >= 0)
            throw ReportError(10, "function redefinition");

        int start = _codeSize;
        int firstVariable = ParseParameters();
        int count = _count - firstVariable;
        ExpectToken("{"u8);
        while (!TryToken("}"u8))
        {
            ParseStatement(null);
        }

        Emit(ScriptOpcode.Return);
        TruncateObjects(saved);
        int index = AddObject(new ScriptObject(name, ScriptObjectKind.Function, start, firstVariable, count));
        if (name.AsSpan().SequenceEqual("main"u8))
        {
            MainIndex = index;
        }
    }

}
