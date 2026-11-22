using System.Buffers;
using System.Buffers.Binary;
using ASToolkit.Core.Engine.IO;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine.Scripts;

public sealed partial class Script
{
    private const int TextPrefix = 0xfe2;
    private const int TextExtra = 0x1000;
    private const int CodeCapacity = 0x3e800;
    private const int MaxNameLength = 0xfff;
    private const int MaxBreaks = 0x80;
    private const int UnsizedArray = 999999;
    private const int ErrorContextLength = 60;

    private readonly Dictionary<byte[], byte[]> _defines = new(ScriptNameComparer.Instance);
    private readonly Dictionary<byte[], byte[]>.AlternateLookup<ReadOnlySpan<byte>> _defineLookup;
    private readonly byte[] _name = new byte[MaxNameLength + 1];
    private IGameFileSystem? _fileSystem;
    private byte[] _text = [];
    private int _position;
    private int _end;
    private int _line;
    private int _ifDepth;
    private bool _isSubstitutionOff;
    private byte[] _codeBuffer = [];
    private int _codeSize;
    private int _nameLength;

    /// <summary>
    /// Gets the path of the script file.
    /// </summary>
    public string FileName { get; private set; } = "";

    /// <summary>
    /// Gets the compile error after which Alien Shooter 2 leaves the script empty, or null.
    /// </summary>
    public string? CompileError { get; private set; }

    /// <summary>
    /// Loads a script file by compiling its source or reading its compiled form.
    /// </summary>
    /// <param name="fileSystem">The file system the script and its includes are read from.</param>
    /// <param name="path">The path of the script file.</param>
    /// <returns><see langword="true"/> when the file was opened; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="InvalidDataException">The source of an Alien Shooter script has an error the engine stops on.</exception>
    [EngineRoutine(0x0041f7d0)]
    public bool Load(IGameFileSystem fileSystem, string path)
    {
        Reset();
        FileName = path;
        if (!fileSystem.TryRead(path, out var file))
        {
            LogScriptError(7, "");

            return false;
        }

        Span<byte> head = stackalloc byte[sizeof(int)];
        file.Span[..Math.Min(file.Length, head.Length)].CopyTo(head);
        if (head[^1] != 0)
        {
            Compile(fileSystem, path, file.Span);
            return true;
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(head);
        EnsureVariableCapacity(Math.Max(count, MinCapacity));
        _count = count;
        int offset = sizeof(int) + ReadState(file[sizeof(int)..]);
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

    [EngineRoutine(0x0041f330)]
    internal void Reset()
    {
        _code = null;
        Array.Clear(_slots);
        Array.Clear(_objects);
        _count = 0;
        _objectCount = 0;
        _defines.Clear();
        _codeSize = 0;
        _ifDepth = 0;
        _isSubstitutionOff = false;
        MainIndex = -1;
        CompileError = null;
    }

    private static bool IsAlpha(byte character) => char.IsAsciiLetter((char)character);

    private static bool IsAlphanumeric(byte character) => char.IsAsciiLetterOrDigit((char)character);

    private static bool IsNameCharacter(byte character) => IsAlphanumeric(character) || character == '_';

    private static string Decode(ReadOnlySpan<byte> text) => GameTextEncoding.Cyrillic.GetString(text);

    private static bool IsScriptEvent(ReadOnlySpan<byte> name) =>
        name.StartsWith("ScriptEvent"u8) && name.Length > 11 && !name[11..].ContainsAnyExceptInRange((byte)'0', (byte)'9');

    [EngineRoutine(0x0041f450)]
    private void Compile(IGameFileSystem fileSystem, string path, ReadOnlySpan<byte> file)
    {
        FileName = path;
        _fileSystem = fileSystem;
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

            Logger?.Write("LoadScript::ByteCode=%i varNo=%i DefineNo=%i stackNo=%i"u8, _codeSize, _objectCount, _defines.Count, _count);
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
        catch (InvalidDataException ex) when (IsAlienShooter2)
        {
            Reset();
            CompileError = ex.Message;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(_codeBuffer);
            _codeBuffer = [];
            _fileSystem = null;
        }
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

    private byte GetCharAt(int index) => index >= 0 && index < _text.Length ? _text[index] : (byte)0;

    [EngineRoutine(0x0041fbe0)]
    private InvalidDataException ReportError(int code, string text, int extra = 0)
    {
        LogScriptError(code, text, extra);

        return new InvalidDataException($"'{FileName}' line {_line + 1}: {GameLogger.FormatError((LogError)code, extra, GameTextEncoding.Cyrillic.GetBytes(text))}");
    }

    private void LogScriptError(int code, string text, int extra = 0)
    {
        byte[] message = GameTextEncoding.Cyrillic.GetBytes(text);
        var logger = Logger;

        if (logger is not null)
        {
            logger.Error("LOGIC '%s' line %i"u8, (LogError)code, extra, message, (ReadOnlyMemory<byte>)GameTextEncoding.Cyrillic.GetBytes(FileName), _line + 1);

            if (_text.Length > 0)
            {
                Span<byte> context = stackalloc byte[ErrorContextLength];
                for (int i = 0; i < context.Length; i++)
                {
                    int address = _position - ErrorContextLength / 2 + i;
                    byte character = address >= 0 && address < _end ? _text[address] : (byte)'?';
                    context[i] = character is (byte)'\n' or (byte)'\r' or (byte)'\t' ? (byte)'?' : character;
                }

                logger.Error("LOGIC"u8, LogError.Text, 0, context);
                context.Fill((byte)' ');
                context[ErrorContextLength / 2] = (byte)'^';
                logger.Error("LOGIC"u8, LogError.Text, 0, context);
            }
        }
    }

    [EngineRoutine(0x0041fcb0)]
    private bool SkipBlanksAndDirectives()
    {
        int mode = 0;
        int skip = 0;
        while (_position < _end)
        {
            if (mode == 0)
            {
                byte character = GetCharAt(_position);
                if ((IsAlpha(character) || character == '_') && !_isSubstitutionOff)
                {
                    Substitute();
                }

                int start = _position;
                if (GetCharAt(start) == '#')
                {
                    skip = ReadConditional(skip);
                }

                character = GetCharAt(_position);
                if (character == '/' && GetCharAt(_position + 1) == '/')
                {
                    mode = 1;
                }
                else if (character == '/' && GetCharAt(_position + 1) == '*')
                {
                    mode = 2;
                }
                else
                {
                    if (character == '?')
                        throw ReportError(10, "?: not supported in this version");

                    if (skip == 0 && !CRuntime.IsWhiteSpace(character) && character != 0)
                        return false;
                }
            }
            else if (mode == 1)
            {
                if (GetCharAt(_position) == '\n')
                {
                    mode = 0;
                }
            }
            else if (GetCharAt(_position) == '/' && GetCharAt(_position - 1) == '*')
            {
                mode = 0;
            }

            if (GetCharAt(_position++) == '\n')
            {
                _line++;
            }
        }

        if (_ifDepth > 0)
        {
            LogScriptError(10, "#ifdef without #endif", _ifDepth);
        }

        return true;
    }

    private void Substitute()
    {
        int length = 0;
        while (IsNameCharacter(GetCharAt(_position + length)))
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
                _isSubstitutionOff = true;
                var name = ReadName();
                _isSubstitutionOff = false;
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
            if (_ifDepth < 0)
            {
                LogScriptError(10, "#endif without #ifdef");
            }
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

            if (_ifDepth < 1)
            {
                LogScriptError(10, "#else without #ifdef");
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

        if ((IsAlpha(token[0]) || token[0] == '#') && IsNameCharacter(GetCharAt(_position + token.Length)))
            return false;

        if (IsAlienShooter2 && token.Length == 1 && GetCharAt(_position + 1) == token[0] && "-+|&="u8.Contains(token[0]))
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
        while (IsNameCharacter(GetCharAt(_position)))
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
        while (GetCharAt(_position) != '\n' && GetCharAt(_position) != '\r')
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
        if (GetCharAt(_position) != '"')
            return 0;

        int count = 0;
        byte character = GetCharAt(++_position);
        while (character != '"' && _position < _end)
        {
            if (character == '\\')
            {
                byte next = GetCharAt(_position + 1);
                if (next == '\r' && GetCharAt(_position + 2) == '\n')
                {
                    _line++;
                    _position += 2;
                }
                else if (next == '\n')
                {
                    _line++;
                    _position++;
                }
                else if (char.IsAsciiDigit((char)next) && char.IsAsciiDigit((char)GetCharAt(_position + 2)) && char.IsAsciiDigit((char)GetCharAt(_position + 3)))
                {
                    output[count++] = (byte)(((next * 8 + GetCharAt(_position + 2) + 10) & 0xff) * 8 + GetCharAt(_position + 3));
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

            character = GetCharAt(++_position);
        }

        if (_end <= _position++)
            throw ReportError(10, "End of file");

        output[count++] = 0;

        return count;
    }

    private void Emit(byte value) => _codeBuffer[_codeSize++] = value;

    private void Emit(ScriptOpcode opcode) => Emit((byte)opcode);

    private void EmitInt(int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(_codeBuffer.AsSpan(_codeSize), value);
        _codeSize += sizeof(int);
    }

    private void Emit(ScriptOpcode opcode, int operand)
    {
        Emit(opcode);
        EmitInt(operand);
    }

    private int ReserveInt()
    {
        int address = _codeSize;
        _codeSize += sizeof(int);

        return address;
    }

    private int ReadCode(int address) => BinaryPrimitives.ReadInt32LittleEndian(_codeBuffer.AsSpan(address));

    private void WriteCode(int address, int value) => BinaryPrimitives.WriteInt32LittleEndian(_codeBuffer.AsSpan(address), value);

    private void PatchHere(int address) => WriteCode(address, _codeSize - address);

    private void EmitJumpBack(ScriptOpcode opcode, int target)
    {
        Emit(opcode);
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
    private void EmitBinaryOp(int start, ScriptOpcode opcode)
    {
        if (_codeBuffer[start] != (byte)ScriptOpcode.PushNumber || _codeBuffer[start + 5] != (byte)ScriptOpcode.PushNumber || _codeSize - start != 10)
        {
            if (!IsAlienShooter2 || opcode != ScriptOpcode.Add || !TryFoldTexts(start))
            {
                Emit(opcode);
            }

            return;
        }

        int left = ReadCode(start + 1);
        int right = ReadCode(start + 6);
        int result = opcode switch
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

    private bool TryFoldTexts(int start)
    {
        if (_codeBuffer[start] != (byte)ScriptOpcode.PushText)
            return false;

        int first = start + 1 + _codeBuffer.AsSpan(start + 1, _codeSize - start - 1).IndexOf((byte)0);
        if (first + 1 >= _codeSize || _codeBuffer[first + 1] != (byte)ScriptOpcode.PushText ||
            first + 2 + _codeBuffer.AsSpan(first + 2, _codeSize - first - 2).IndexOf((byte)0) != _codeSize - 1)
            return false;

        _codeBuffer.AsSpan(first + 2, _codeSize - first - 2).CopyTo(_codeBuffer.AsSpan(first));
        _codeSize -= 2;

        return true;
    }

    [EngineRoutine(0x00421f60)]
    private void ParseExpression()
    {
        if (IsAlienShooter2)
        {
            ParseLogicalOr();
            return;
        }

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

    private void ParseLogicalOr()
    {
        ParseLogicalAnd();
        while (TryToken("||"u8))
        {
            Emit(ScriptOpcode.OrJump);
            int skip = ReserveInt();
            ParseLogicalAnd();
            Emit(ScriptOpcode.LogicalOr);
            PatchHere(skip);
        }
    }

    private void ParseLogicalAnd()
    {
        ParseBitwiseOr();
        while (TryToken("&&"u8))
        {
            Emit(ScriptOpcode.AndJump);
            int skip = ReserveInt();
            ParseBitwiseOr();
            Emit(ScriptOpcode.LogicalAnd);
            PatchHere(skip);
        }
    }

    private void ParseBitwiseOr()
    {
        int start = _codeSize;
        ParseBitwiseXor();
        while (TryToken("|"u8))
        {
            ParseBitwiseXor();
            EmitBinaryOp(start, ScriptOpcode.Or);
        }
    }

    private void ParseBitwiseXor()
    {
        int start = _codeSize;
        ParseBitwiseAnd();
        while (TryToken("^"u8))
        {
            ParseBitwiseAnd();
            EmitBinaryOp(start, ScriptOpcode.Xor);
        }
    }

    private void ParseBitwiseAnd()
    {
        int start = _codeSize;
        ParseComparison();
        while (TryToken("&"u8))
        {
            ParseComparison();
            EmitBinaryOp(start, ScriptOpcode.And);
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
        var cast = ScriptOpcode.None;
        while (true)
        {
            var access = ScriptOpcode.Load;
            if (IsAlienShooter2)
            {
                cast = ParseCast(cast);
            }

            var unary = ParseUnary();

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

            byte character = GetCharAt(_position);
            if (char.IsAsciiDigit((char)character))
            {
                int value = CRuntime.TryScanInteger(ReadName(), out int scanned) ? scanned : (int)access;
                value = unary switch
                {
                    ScriptOpcode.Negate => unchecked(-value),
                    ScriptOpcode.BitwiseNot => ~value,
                    ScriptOpcode.LogicalNot => value == 0 ? 1 : 0,
                    _ => value
                };
                Emit(ScriptOpcode.PushNumber, value);
                EmitOptional(cast);
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
                EmitInt((sbyte)GetCharAt(_position++));
                if (GetCharAt(_position) != '\'')
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
                    ParseIntDeclarators(false);
                }
                else if (TryToken("string"u8))
                {
                    ParseStringDeclarators(false);
                }
                else
                    throw ReportError(4, "static variable");
            }
            else if (TryToken("int"u8))
            {
                ParseIntDeclarators(IsAlienShooter2);
            }
            else if (TryToken("string"u8))
            {
                ParseStringDeclarators(IsAlienShooter2);
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
            else if (IsAlpha(GetCharAt(_position)))
            {
                if (!ParseName(unary, cast, access))
                    continue;

                return;
            }
            else
            {
                if (unary != ScriptOpcode.None || GetCharAt(_position) == 0 || ".$#@`"u8.Contains(GetCharAt(_position)))
                    throw ReportError(10, "error symbol");

                return;
            }

            EmitOptional(unary);
            EmitOptional(cast);
            return;
        }
    }

    private ScriptOpcode ParseCast(ScriptOpcode cast)
    {
        if (TryToken("(int)"u8))
            return ScriptOpcode.CastInt;
        
        if (TryToken("(string)"u8))
            return ScriptOpcode.CastString;
        
        if (TryToken("(sprite)"u8))
            return ScriptOpcode.CastSprite;

        return cast;
    }

    private ScriptOpcode ParseUnary()
    {
        if (IsAlienShooter2 ? TryToken("-"u8) : GetCharAt(_position + 1) != '-' && TryToken("-"u8))
            return ScriptOpcode.Negate;
        
        if (IsAlienShooter2 && TryToken("+"u8))
            return ScriptOpcode.None;
        
        if (TryToken("~"u8))
            return ScriptOpcode.BitwiseNot;
        
        if (TryToken("!"u8))
            return ScriptOpcode.LogicalNot;

        return ScriptOpcode.None;
    }

    private void EmitOptional(ScriptOpcode opcode)
    {
        if (opcode != ScriptOpcode.None)
        {
            Emit(opcode);
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

    private bool ParseName(ScriptOpcode unary, ScriptOpcode cast, ScriptOpcode access)
    {
        var name = ReadName();
        int found = FindObject(name);
        if (found < 0)
        {
            if (GetCharAt(_position) != ':')
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
                throw ReportError(10, GetCharAt(_position) == ':' ? $"Label redefinition '{Decode(name)}'" : $"Incorrect use label '{Decode(name)}'");
            case ScriptObjectKind.PendingLabel:
                if (GetCharAt(_position) != ':')
                    throw ReportError(10, $"Incorrect use label '{Decode(name)}'");

                _position++;
                target.Kind = ScriptObjectKind.Label;
                PatchHere(target.Address);
                target.Address = _codeSize;
                return false;
        }

        EmitOptional(unary);
        EmitOptional(cast);

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

        var opcode = access;
        var compound = ScriptOpcode.None;
        if (GetCharAt(_position + 1) != '=' && TryToken("="u8))
        {
            ParseExpression();
            opcode = ScriptOpcode.Assign;
        }
        else if ((compound = TryCompoundToken()) != ScriptOpcode.None)
        {
            ParseExpression();
            opcode = ScriptOpcode.CompoundAssign;
        }
        else if (TryToken("++"u8))
        {
            opcode = ScriptOpcode.PostIncrement;
        }
        else if (TryToken("--"u8))
        {
            opcode = ScriptOpcode.PostDecrement;
        }

        if (index is not null)
        {
            index.AsSpan(0, indexLength).CopyTo(_codeBuffer.AsSpan(_codeSize));
            _codeSize += indexLength;
            ArrayPool<byte>.Shared.Return(index);
        }
        else if ((_slots[variable].Flags & (StackObjectFlags.ArrayElement | StackObjectFlags.Pointer)) != 0)
        {
            if (opcode is >= ScriptOpcode.PostIncrement and <= ScriptOpcode.PreDecrement)
                throw ReportError(10, "Increment or decrement for array");

            if (opcode != ScriptOpcode.Load)
                throw ReportError(4, "operation for array", (int)opcode);
        }

        Emit(opcode, variable);
        if (opcode == ScriptOpcode.CompoundAssign)
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
        while (!TryToken(")"u8))
        {
            ParseExpression();
            TryToken(","u8);
            count++;
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
        while (!TryToken(")"u8))
        {
            ParseExpression();
            TryToken(","u8);
            EmitArgumentStore(target.FirstVariable + count++);
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

        Emit(ScriptOpcode.Call, IsAlienShooter2 ? found : target.Address);
    }

    private void EmitArgumentStore(int variable)
    {
        Emit(ScriptOpcode.Assign, variable);
        Emit(ScriptOpcode.Pop);
    }

    private void ParseIntDeclarators(bool initializeAtRunTime)
    {
        do
        {
            ParseIntDeclarator(initializeAtRunTime);
        }
        while (TryToken(","u8));
    }

    private void ParseStringDeclarators(bool initializeAtRunTime)
    {
        do
        {
            ParseStringDeclarator(initializeAtRunTime);
        }
        while (TryToken(","u8));
    }

    [EngineRoutine(0x00420720)]
    private void ParseIntDeclarator(bool initializeAtRunTime = false)
    {
        var flags = StackObjectFlags.None;
        SkipOrFailAtEnd();
        if (GetCharAt(_position) == '*')
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
            if (GetCharAt(_position) == ']')
            {
                _position++;
                if (!TryToken("="u8))
                    throw ReportError(10, "for [] need initialisation");

                ExpectToken("{"u8);
                int count = 0;
                do
                {
                    if (initializeAtRunTime)
                    {
                        Push(new StackObject(StackObjectFlags.Number | flags | StackObjectFlags.NotInitialized, 0, default));
                        EmitInitializer(_count - 1);
                        count++;
                        continue;
                    }

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
                ParseArrayInitializer(size, initializeAtRunTime);
            }
            else if (initializeAtRunTime)
            {
                EmitInitializer(_count - 1);
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

    private void ParseArrayInitializer(int size, bool initializeAtRunTime)
    {
        ExpectToken("{"u8);
        for (int element = 0; element < size; element++)
        {
            if (initializeAtRunTime)
            {
                EmitInitializer(_count - size + element);
                if (!TryToken(","u8))
                {
                    ExpectToken("}"u8);
                    return;
                }

                continue;
            }

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

    private void EmitInitializer(int variable)
    {
        ParseExpression();
        Emit(ScriptOpcode.Assign, variable);
        Emit(ScriptOpcode.EndStatement, _line);
    }

    [EngineRoutine(0x00420aa0)]
    private void ParseStringDeclarator(bool initializeAtRunTime = false)
    {
        if (IsAlienShooter2)
        {
            SkipOrFailAtEnd();
            if (GetCharAt(_position) == '*')
            {
                _position++;
            }
        }

        var name = ReadName();
        if (FindObject(name) >= 0)
            throw ReportError(10, $"string redifinition '{Decode(name)}'");

        byte[] symbolName = name.ToArray();
        var flags = StackObjectFlags.None;
        int size = 1;
        if (TryToken("["u8))
        {
            flags = StackObjectFlags.ArrayElement;
            size = IsAlienShooter2 && GetCharAt(_position) == ']' ? UnsizedArray : ParseConstInt();
            ExpectToken("]"u8);
        }

        int symbol = AddObject(new ScriptObject(symbolName, ScriptObjectKind.Variable, _count));
        int count = 0;
        if (TryToken("="u8))
        {
            bool braced = IsAlienShooter2 && flags.HasFlag(StackObjectFlags.ArrayElement);
            if (braced)
            {
                ExpectToken("{"u8);
            }

            for (; !IsAlienShooter2 && count < size - 1; count++)
            {
                Push(new StackObject(StackObjectFlags.Text | flags, 0, default));
            }

            do
            {
                if (count >= size)
                    throw ReportError(10, "too many initializers");

                if (initializeAtRunTime)
                {
                    Push(new StackObject(StackObjectFlags.Text | flags | StackObjectFlags.NotInitialized, 0, default));
                    EmitInitializer(_count - 1);
                }
                else
                {
                    Push(new StackObject(StackObjectFlags.Text | flags | StackObjectFlags.Initialized, 0, IsAlienShooter2 ? ParseConstText() : ParseLiteralText()));
                }

                count++;
            }
            while (braced && TryToken(","u8));

            if (braced)
            {
                ExpectToken("}"u8);
            }
        }
        else if (size == UnsizedArray)
            throw ReportError(10, "for [] need initialisation");

        if (size == UnsizedArray)
        {
            size = count;
        }

        for (; count < size; count++)
        {
            Push(new StackObject(StackObjectFlags.Text | flags, 0, default));
        }

        _objects[symbol].Count = size;
    }

    private byte[] ParseLiteralText()
    {
        Span<byte> literal = stackalloc byte[MaxNameLength + 1];
        int length = ParseStringLiteral(literal);

        return length > 0 ? literal[..(length - 1)].ToArray() : [];
    }

    private byte[] ParseConstText()
    {
        SkipOrFailAtEnd();
        int start = _codeSize;
        ParseExpression();
        int length = _codeBuffer[start] == (byte)ScriptOpcode.PushText ? _codeBuffer.AsSpan(start + 1, _codeSize - start - 1).IndexOf((byte)0) : -1;
        if (length < 0 || _codeSize - start != length + 2)
            throw ReportError(4, "constant string value");

        _codeSize = start;

        return _codeBuffer.AsSpan(start + 1, length).ToArray();
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
            Emit(ScriptOpcode.EndStatement, IsAlienShooter2 ? _line + 1 : _line);
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
        foreach (int address in breaks)
        {
            PatchHere(address);
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
            _isSubstitutionOff = true;
            byte[] name = ReadName().ToArray();
            _isSubstitutionOff = false;
            _defines[name] = ReadDefineLine();

            return SkipBlanksAndDirectives();
        }

        if (text.StartsWith("#undef"u8))
        {
            _position += 6;
            _isSubstitutionOff = true;
            bool found = _defineLookup.Remove(ReadName());
            _isSubstitutionOff = false;
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
                ParseIntDeclarators(false);
            }
            else if (TryToken("string"u8))
            {
                ParseStringDeclarators(false);
            }
            else
                throw ReportError(4, "static variable");

            ExpectToken(";"u8);
            return SkipBlanksAndDirectives();
        }

        if (TryToken("int"u8))
        {
            ParseIntDeclarators(false);
            ExpectToken(";"u8);
            return SkipBlanksAndDirectives();
        }

        if (TryToken("string"u8))
        {
            ParseStringDeclarators(false);
            ExpectToken(";"u8);
            return SkipBlanksAndDirectives();
        }

        ParseFunction();

        return SkipBlanksAndDirectives();
    }

    private void ParseInclude()
    {
        string includer = FileName;
        if (GetCharAt(_position) != '"' && GetCharAt(_position) != '<')
            throw ReportError(13, "include file name");

        int start = ++_position;
        while (GetCharAt(_position) != '"' && GetCharAt(_position) != '>')
        {
            if (_position >= _end)
                throw ReportError(10, "End of file");

            _position++;
        }

        string path = Decode(_text.AsSpan(start, _position++ - start));
        if (!_fileSystem!.TryRead(path, out var file))
            throw ReportError(7, path);

        (byte[] text, int position, int end, int line, int depth) = (_text, _position, _end, _line, _ifDepth);
        OpenText(file.Span);
        FileName = path;
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
            FileName = includer;
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
        if (!char.IsAsciiDigit((char)GetCharAt(_position - 1)))
            throw ReportError(13, "extern function code");

        TruncateObjects(saved);
        AddObject(new ScriptObject(name, ScriptObjectKind.Extern, id, firstVariable, _count - firstVariable));
        ExpectToken(";"u8);
    }

    private int ParseParameters(int prototype = -1)
    {
        int firstVariable = _count;
        ExpectToken("("u8);
        int parameter = 0;
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
            else
                continue;

            if (prototype >= 0)
            {
                _objects[_objectCount - 1].Address = _objects[prototype].FirstVariable + parameter;
            }

            parameter++;
        }
        while (TryToken(","u8));

        ExpectToken(")"u8);

        return firstVariable;
    }

    private void ParseFunction()
    {
        int saved = _objectCount;
        byte[] name = ReadName().ToArray();
        int prototype = FindObject(name);
        if (prototype >= 0 && !IsAlienShooter2)
            throw ReportError(10, "function redefinition");

        int start = _codeSize;
        int firstVariable = ParseParameters(prototype);
        int count = _count - firstVariable;
        if (prototype >= 0)
        {
            if (_objects[prototype].Kind != ScriptObjectKind.Function || _objects[prototype].Count != count || _objects[prototype].Address != -1)
                throw ReportError(10, "function redefinition");

            _count = firstVariable;
        }

        int address = -1;
        if (!IsAlienShooter2 || !TryToken(";"u8))
        {
            address = start;
            if (prototype >= 0)
            {
                _objects[prototype].Address = start;
            }

            ExpectToken("{"u8);
            while (!TryToken("}"u8))
            {
                ParseStatement(null);
            }

            Emit(ScriptOpcode.Return);
        }

        TruncateObjects(saved);
        if (prototype >= 0)
            return;

        int index = AddObject(new ScriptObject(name, ScriptObjectKind.Function, address, firstVariable, count));
        if (name.AsSpan().SequenceEqual("main"u8))
        {
            MainIndex = index;
        }
        else if (IsAlienShooter2 && IsScriptEvent(name) && count != 3)
            throw ReportError(10, "ScriptEvent must have 3 parameters");
    }
}
