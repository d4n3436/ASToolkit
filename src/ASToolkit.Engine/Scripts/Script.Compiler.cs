using System.Buffers;
using System.Buffers.Binary;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine.Scripts;

public sealed partial class Script
{
    private const int TextPrefix = 0xfe2;
    private const int TextExtra = 0x1000;
    private const int CodeCapacity = 0x3e800;
    private const int MaxNameLength = 0xfff;
    private const int MaxBreaks = 0x80;
    private const int UnsizedArray = 999999;
    private const int ErrorContextLength = 60;

    private readonly NamedList<ReadOnlyMemory<byte>> _defines = new();
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
    /// Gets the error that kept an Alien Shooter 2 script from compiling and left it empty, or null if there wasn't one.
    /// </summary>
    public string? CompileError { get; private set; }

    /// <summary>
    /// Loads a script file by compiling its source or reading its compiled form.
    /// </summary>
    /// <param name="fileSystem">The file system that holds the script and its includes.</param>
    /// <param name="path">The path of the script file.</param>
    /// <returns><see langword="true"/> when the file was opened; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="InvalidDataException">The source doesn't compile and the script isn't an Alien Shooter 2 script. Alien Shooter 2 scripts record the error in <see cref="CompileError"/> instead.</exception>
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
            LoadLgc(fileSystem, path, file.Span);
            return true;
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(head);
        _stack.Expand(Math.Max(count, MinCapacity));
        _stack.Count = count;
        int offset = sizeof(int) + LoadVar(file[sizeof(int)..]);
        int size = BinaryPrimitives.ReadInt32LittleEndian(file.Span[offset..]);
        offset += sizeof(int);
        _code = file.Slice(offset, size).ToArray();
        for (int i = 0; i < _objects.Count; i++)
        {
            if (_objects.Name(i).Span.SequenceEqual("main"u8))
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
        _stack.Release();
        _objects.Release();
        _defines.Release();
        _codeSize = 0;
        _ifDepth = 0;
        _isSubstitutionOff = false;
        MainIndex = -1;
        CompileError = null;
    }

    private static string Decode(ReadOnlySpan<byte> text) => GameTextEncoding.Cyrillic.GetString(text);

    private static bool IsScriptEvent(ReadOnlySpan<byte> name) =>
        name.StartsWith("ScriptEvent"u8) && name.Length > 11 && !name[11..].ContainsAnyExceptInRange((byte)'0', (byte)'9');

    private bool IsSpace(byte character) => IsAlienShooter2 ? CRuntime.IsWhiteSpace(character) : CRuntime.IsSpace(character);

    private bool IsDigit(byte character) => IsAlienShooter2 ? char.IsAsciiDigit((char)character) : CRuntime.IsDigit(character);

    private bool IsAlpha(byte character) => IsAlienShooter2 ? char.IsAsciiLetter((char)character) : CRuntime.IsAlpha(character);

    private bool IsAlphanumeric(byte character) => IsAlienShooter2 ? char.IsAsciiLetterOrDigit((char)character) : CRuntime.IsAlphanumeric(character);

    private bool IsNameCharacter(byte character) => IsAlphanumeric(character) || character == '_';

    [EngineRoutine(0x0041f450)]
    private void LoadLgc(IGameFileSystem fileSystem, string path, ReadOnlySpan<byte> file)
    {
        FileName = path;
        _fileSystem = fileSystem;
        _codeBuffer = ArrayPool<byte>.Shared.Rent(CodeCapacity);
        _codeSize = 0;
        _stack.Expand(MinCapacity);
        _objects.Expand(MinCapacity);
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

            Error?.Log($"LoadScript::ByteCode={_codeSize} varNo={_objects.Count} DefineNo={_defines.Count} stackNo={_stack.Count}");
            _defines.Release();
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

    [EngineRoutine(0x00424f70)]
    private InvalidDataException ReportError(int code, string text, int extra = 0)
    {
        LogScriptError(code, text, extra);

        return new InvalidDataException($"'{FileName}' line {_line + 1}: {MyError.FormatError((LogError)code, extra, GameTextEncoding.Cyrillic.GetBytes(text))}");
    }

    [EngineRoutine(0x0041fbe0)]
    private void LogScriptError(int code, string text, int extra = 0)
    {
        byte[] message = GameTextEncoding.Cyrillic.GetBytes(text);
        var error = Error;

        if (error is not null)
        {
            error.Error($"LOGIC '{FileName}' line {_line + 1}", (LogError)code, extra, message);

            if (_text.Length > 0)
            {
                Span<byte> context = stackalloc byte[ErrorContextLength];
                for (int i = 0; i < context.Length; i++)
                {
                    int address = _position - ErrorContextLength / 2 + i;
                    byte character = _text[address];
                    context[i] = character is (byte)'\n' or (byte)'\r' or (byte)'\t' ? (byte)'?' : character;
                }

                int terminator = context.IndexOf((byte)0);
                error.Error("LOGIC"u8, LogError.Text, 0, terminator < 0 ? context : context[..terminator]);
                context.Fill((byte)' ');
                context[ErrorContextLength / 2] = (byte)'^';
                error.Error("LOGIC"u8, LogError.Text, 0, context);
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
                byte character = _text[_position];
                if ((IsAlpha(character) || character == '_') && !_isSubstitutionOff)
                {
                    Substitute();
                }

                if (_text[_position] == '#')
                {
                    skip = ReadConditional(skip);
                }

                character = _text[_position];
                if (character == '/' && _text[_position + 1] == '/')
                {
                    mode = 1;
                }
                else if (character == '/' && _text[_position + 1] == '*')
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
                if (_text[_position] == '\n')
                {
                    mode = 0;
                }
            }
            else if (_text[_position] == '/' && _text[_position - 1] == '*')
            {
                mode = 0;
            }

            if (_text[_position++] == '\n')
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
        while (IsNameCharacter(_text[_position + length]))
        {
            if (length >= MaxNameLength)
                throw ReportError(10, "Very long name");

            length++;
        }

        int found = _defines.Location(_text.AsSpan(_position, length));
        if (found >= 0)
        {
            var value = _defines[found].Span;
            _position = _position + length - value.Length;
            value.CopyTo(_text.AsSpan(_position));
        }
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
                bool defined = _objects.Location(name) >= 0 || _defines.Location(name) >= 0;
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
    private bool TryWord(ReadOnlySpan<byte> token)
    {
        SkipOrFailAtEnd();
        if (!_text.AsSpan(_position).StartsWith(token))
            return false;

        if ((IsAlpha(token[0]) || token[0] == '#') && IsNameCharacter(_text[_position + token.Length]))
            return false;

        if (IsAlienShooter2 && token.Length == 1 && _text[_position + 1] == token[0] && "-+|&="u8.Contains(token[0]))
            return false;

        _position += token.Length;
        SkipBlanksAndDirectives();

        return true;
    }

    [EngineRoutine(0x004204f0)]
    private void ExpectToken(ReadOnlySpan<byte> token)
    {
        if (!TryWord(token))
            throw ReportError(13, Decode(token));
    }

    [EngineRoutine(0x004203b0)]
    private ReadOnlySpan<byte> ReadName()
    {
        SkipOrFailAtEnd();
        int length = 0;
        while (IsNameCharacter(_text[_position]))
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
    private byte[] GetLine()
    {
        SkipOrFailAtEnd();
        int start = _position;
        while (_text[_position] != '\n' && _text[_position] != '\r')
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
    private int PutString(Span<byte> output)
    {
        if (_text[_position] != '"')
            return 0;

        int count = 0;
        byte character = _text[++_position];
        while (character != '"' && _position < _end)
        {
            if (character == '\\')
            {
                byte next = _text[_position + 1];
                if (next == '\r' && _text[_position + 2] == '\n')
                {
                    _line++;
                    _position += 2;
                }
                else if (next == '\n')
                {
                    _line++;
                    _position++;
                }
                else if (IsDigit(next) && IsDigit(_text[_position + 2]) && IsDigit(_text[_position + 3]))
                {
                    output[count++] = (byte)(((next * 8 + _text[_position + 2] + 10) & 0xff) * 8 + _text[_position + 3]);
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

            character = _text[++_position];
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
    private void SetOperation(int start, ScriptOpcode opcode)
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
            if (TryWord("^"u8))
            {
                ParseComparison();
                SetOperation(start, ScriptOpcode.Xor);
            }
            else if (TryWord("&&"u8))
            {
                ParseComparison();
                Emit(ScriptOpcode.LogicalAnd);
            }
            else if (TryWord("&"u8))
            {
                ParseComparison();
                SetOperation(start, ScriptOpcode.And);
            }
            else if (TryWord("||"u8))
            {
                ParseComparison();
                Emit(ScriptOpcode.LogicalOr);
            }
            else if (TryWord("|"u8))
            {
                ParseComparison();
                SetOperation(start, ScriptOpcode.Or);
            }
            else
                break;
        }
    }

    private void ParseLogicalOr()
    {
        ParseLogicalAnd();
        while (TryWord("||"u8))
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
        while (TryWord("&&"u8))
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
        while (TryWord("|"u8))
        {
            ParseBitwiseXor();
            SetOperation(start, ScriptOpcode.Or);
        }
    }

    private void ParseBitwiseXor()
    {
        int start = _codeSize;
        ParseBitwiseAnd();
        while (TryWord("^"u8))
        {
            ParseBitwiseAnd();
            SetOperation(start, ScriptOpcode.Xor);
        }
    }

    private void ParseBitwiseAnd()
    {
        int start = _codeSize;
        ParseComparison();
        while (TryWord("&"u8))
        {
            ParseComparison();
            SetOperation(start, ScriptOpcode.And);
        }
    }

    [EngineRoutine(0x00421e10)]
    private void ParseComparison()
    {
        int start = _codeSize;
        ParseAdditive();
        while (true)
        {
            if (TryWord(">="u8))
            {
                ParseAdditive();
                Emit(ScriptOpcode.GreaterOrEqual);
            }
            else if (TryWord(">>"u8))
            {
                ParseAdditive();
                SetOperation(start, ScriptOpcode.ShiftRight);
            }
            else if (TryWord(">"u8))
            {
                ParseAdditive();
                Emit(ScriptOpcode.Greater);
            }
            else if (TryWord("<="u8))
            {
                ParseAdditive();
                Emit(ScriptOpcode.LessOrEqual);
            }
            else if (TryWord("<<"u8))
            {
                ParseAdditive();
                SetOperation(start, ScriptOpcode.ShiftLeft);
            }
            else if (TryWord("<"u8))
            {
                ParseAdditive();
                Emit(ScriptOpcode.Less);
            }
            else if (TryWord("=="u8))
            {
                ParseAdditive();
                Emit(ScriptOpcode.Equal);
            }
            else if (TryWord("!="u8))
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
            if (TryWord("+"u8))
            {
                ParseMultiplicative();
                SetOperation(start, ScriptOpcode.Add);
            }
            else if (TryWord("-"u8))
            {
                ParseMultiplicative();
                SetOperation(start, ScriptOpcode.Subtract);
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
            if (TryWord("*"u8))
            {
                ParseTerm();
                SetOperation(start, ScriptOpcode.Multiply);
            }
            else if (TryWord("/"u8))
            {
                ParseTerm();
                SetOperation(start, ScriptOpcode.Divide);
            }
            else if (TryWord("%"u8))
            {
                ParseTerm();
                SetOperation(start, ScriptOpcode.Modulo);
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
            if (TryWord("--"u8))
            {
                access = ScriptOpcode.PreDecrement;
            }
            else if (TryWord("++"u8))
            {
                access = ScriptOpcode.PreIncrement;
            }
            else if (TryWord("&"u8))
            {
                access = ScriptOpcode.LoadTextAddress;
            }

            byte character = _text[_position];
            if (IsDigit(character))
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
                _codeSize += PutString(_codeBuffer.AsSpan(_codeSize));
            }
            else if (character == '\'')
            {
                Emit(ScriptOpcode.PushNumber);
                _position++;
                EmitInt((sbyte)_text[_position++]);
                if (_text[_position] != '\'')
                    throw ReportError(13, "second '");

                _position++;
            }
            else if (TryWord("sizeof"u8))
            {
                ParseSizeof();
            }
            else if (TryWord("static"u8))
            {
                ParseStaticDeclarators();
            }
            else if (TryWord("int"u8))
            {
                ParseIntDeclarators(IsAlienShooter2);
            }
            else if (TryWord("string"u8))
            {
                ParseStringDeclarators(IsAlienShooter2);
            }
            else if (TryWord("return"u8))
            {
                ParseExpression();
                Emit(ScriptOpcode.Return);
            }
            else if (TryWord("("u8))
            {
                ParseExpression();
                ExpectToken(")"u8);
            }
            else if (IsAlpha(_text[_position]))
            {
                if (!ParseName(access))
                    continue;
            }
            else
            {
                if (unary != ScriptOpcode.None || _text[_position] == 0 || ".$#@`"u8.Contains(_text[_position]))
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
        if (TryWord("(int)"u8))
            return ScriptOpcode.CastInt;

        if (TryWord("(string)"u8))
            return ScriptOpcode.CastString;

        if (TryWord("(sprite)"u8))
            return ScriptOpcode.CastSprite;

        return cast;
    }

    private ScriptOpcode ParseUnary()
    {
        if (IsAlienShooter2 ? TryWord("-"u8) : _text[_position + 1] != '-' && TryWord("-"u8))
            return ScriptOpcode.Negate;

        if (IsAlienShooter2 && TryWord("+"u8))
            return ScriptOpcode.None;

        if (TryWord("~"u8))
            return ScriptOpcode.BitwiseNot;

        if (TryWord("!"u8))
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

    private void EmitPushText(ReadOnlySpan<byte> text)
    {
        Emit(ScriptOpcode.PushText);
        text.CopyTo(_codeBuffer.AsSpan(_codeSize));
        _codeSize += text.Length;
        Emit((byte)0);
    }

    private void ParseSizeof()
    {
        if (!TryWord("("u8))
            throw ReportError(13, "'(' for sizeof");

        Emit(ScriptOpcode.PushNumber);
        if (TryWord("int"u8) || TryWord("string"u8))
        {
            EmitInt(sizeof(int));
        }
        else
        {
            int sized = _objects.Location(ReadName());
            if (sized < 0 || _objects[sized].Kind != ScriptObjectKind.Variable)
                throw ReportError(4, "sizeof parameter");

            EmitInt(_objects[sized].Count << 2);
        }

        ExpectToken(")"u8);
    }

    private void ParseStaticDeclarators()
    {
        if (TryWord("int"u8))
        {
            ParseIntDeclarators(false);
        }
        else if (TryWord("string"u8))
        {
            ParseStringDeclarators(false);
        }
        else
            throw ReportError(4, "static variable");
    }

    private void ParseIntDeclarators(bool initializeAtRunTime)
    {
        do
        {
            IntVar(initializeAtRunTime);
        }
        while (TryWord(","u8));
    }

    private void ParseStringDeclarators(bool initializeAtRunTime)
    {
        do
        {
            ParseStringDeclarator(initializeAtRunTime);
        }
        while (TryWord(","u8));
    }

    private bool ParseName(ScriptOpcode access)
    {
        var name = ReadName();
        int found = _objects.Location(name);
        if (found < 0)
        {
            if (_text[_position] != ':')
                throw ReportError(10, $"Undeclared identifier '{Decode(name)}'");

            byte[] label = name.ToArray();
            _position++;
            _objects.Insert(label, new ScriptObject(ScriptObjectKind.Label, _codeSize));
            return false;
        }

        switch (_objects[found].Kind)
        {
            case ScriptObjectKind.Variable:
                ParseVariable(_objects[found].Address, access);
                break;
            case ScriptObjectKind.Extern:
                ParseExternCall(found);
                break;
            case ScriptObjectKind.Function:
                ParseFunctionCall(found);
                break;
            case ScriptObjectKind.Text:
                EmitPushText(_objects[found].Text.Span);
                break;
            case ScriptObjectKind.Number:
                Emit(ScriptOpcode.PushNumber, _objects[found].Address);
                break;
            case ScriptObjectKind.Label:
                throw ReportError(10, _text[_position] == ':' ? $"Label redefinition '{Decode(name)}'" : $"Incorrect use label '{Decode(name)}'");
            case ScriptObjectKind.PendingLabel:
                if (_text[_position] != ':')
                    throw ReportError(10, $"Incorrect use label '{Decode(name)}'");

                _position++;
                _objects[found].Kind = ScriptObjectKind.Label;
                PatchHere(_objects[found].Address);
                _objects[found].Address = _codeSize;
                return false;
        }

        return true;
    }

    private void ParseVariable(int variable, ScriptOpcode access)
    {
        int indexLength = 0;
        byte[]? index = null;
        if (TryWord("["u8))
        {
            if (!_stack[variable].Flags.HasFlag(ScriptStackFlags.Text) && !_stack[variable].Flags.HasFlag(ScriptStackFlags.ArrayElement) && !_stack[variable].Flags.HasFlag(ScriptStackFlags.Pointer))
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
        if (_text[_position + 1] != '=' && TryWord("="u8))
        {
            ParseExpression();
            opcode = ScriptOpcode.Assign;
        }
        else
        {
            compound = ParseCompoundOperator();
            if (compound != ScriptOpcode.None)
            {
                ParseExpression();
                opcode = ScriptOpcode.CompoundAssign;
            }
            else if (TryWord("++"u8))
            {
                opcode = ScriptOpcode.PostIncrement;
            }
            else if (TryWord("--"u8))
            {
                opcode = ScriptOpcode.PostDecrement;
            }
        }

        if (index is not null)
        {
            index.AsSpan(0, indexLength).CopyTo(_codeBuffer.AsSpan(_codeSize));
            _codeSize += indexLength;
            ArrayPool<byte>.Shared.Return(index);
        }
        else if (_stack[variable].Flags.HasFlag(ScriptStackFlags.ArrayElement) || _stack[variable].Flags.HasFlag(ScriptStackFlags.Pointer))
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

    private ScriptOpcode ParseCompoundOperator()
    {
        if (TryWord("+="u8))
            return ScriptOpcode.Add;

        if (TryWord("-="u8))
            return ScriptOpcode.Subtract;

        if (TryWord("/="u8))
            return ScriptOpcode.Divide;

        if (TryWord("*="u8))
            return ScriptOpcode.Multiply;

        if (TryWord("%="u8))
            return ScriptOpcode.Modulo;

        if (TryWord("&="u8))
            return ScriptOpcode.And;

        if (TryWord("|="u8))
            return ScriptOpcode.Or;

        if (TryWord("^="u8))
            return ScriptOpcode.Xor;

        if (TryWord("<<="u8))
            return ScriptOpcode.ShiftLeft;

        if (TryWord(">>="u8))
            return ScriptOpcode.ShiftRight;

        return ScriptOpcode.None;
    }

    private void ParseExternCall(int found)
    {
        var target = _objects[found];
        ExpectToken("("u8);
        int count = 0;
        while (!TryWord(")"u8))
        {
            ParseExpression();
            TryWord(","u8);
            count++;
        }

        for (; count < target.Count && _stack[target.FirstVariable + count].Flags.HasFlag(ScriptStackFlags.Initialized); count++)
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
        while (!TryWord(")"u8))
        {
            ParseExpression();
            TryWord(","u8);
            EmitArgumentStore(target.FirstVariable + count++);
        }

        for (; count < target.Count && _stack[target.FirstVariable + count].Flags.HasFlag(ScriptStackFlags.Initialized); count++)
        {
            ref readonly var parameter = ref _stack[target.FirstVariable + count];
            if (!parameter.Flags.HasFlag(ScriptStackFlags.Text))
            {
                Emit(ScriptOpcode.PushNumber, parameter.Value);
            }
            else
            {
                EmitPushText(parameter.Text.Span);
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

    [EngineRoutine(0x00420720)]
    private void IntVar(bool initializeAtRunTime = false)
    {
        var flags = ScriptStackFlags.None;
        SkipOrFailAtEnd();
        if (_text[_position] == '*')
        {
            flags = ScriptStackFlags.Pointer;
            _position++;
        }

        var name = ReadName();
        if (_objects.Location(name) >= 0)
            throw ReportError(10, $"int redefinition '{Decode(name)}'");

        int symbol = _objects.Insert(name.ToArray(), new ScriptObject(ScriptObjectKind.Variable, _stack.Count));
        int size;
        if (TryWord("["u8))
        {
            flags |= ScriptStackFlags.ArrayElement;
            if (_text[_position] == ']')
            {
                _position++;
                if (!TryWord("="u8))
                    throw ReportError(10, "for [] need initialisation");

                int count = ParseUnsizedArrayInitializer(flags, initializeAtRunTime);
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
            _stack.Insert(new ScriptStack(ScriptStackFlags.Number | flags | ScriptStackFlags.NotInitialized, 0));
        }

        if (TryWord("="u8))
        {
            if (flags.HasFlag(ScriptStackFlags.ArrayElement))
            {
                ParseArrayInitializer(size, initializeAtRunTime);
            }
            else if (initializeAtRunTime)
            {
                EmitInitializer(_stack.Count - 1);
            }
            else
            {
                int value = ParseConstInt();
                ref var variable = ref _stack[_stack.Count - 1];
                variable.Value = value;
                variable.Flags = (variable.Flags | ScriptStackFlags.Initialized) & ~ScriptStackFlags.NotInitialized;
            }
        }

        _objects[symbol].Count = size;
    }

    private int ParseUnsizedArrayInitializer(ScriptStackFlags flags, bool initializeAtRunTime)
    {
        ExpectToken("{"u8);
        int count = 0;
        do
        {
            if (initializeAtRunTime)
            {
                _stack.Insert(new ScriptStack(ScriptStackFlags.Number | flags | ScriptStackFlags.NotInitialized, 0));
                EmitInitializer(_stack.Count - 1);
                count++;
                continue;
            }

            _stack.Insert(new ScriptStack(ScriptStackFlags.Number | flags, 0));
            int value = ParseConstInt();
            ref var element = ref _stack[_stack.Count - 1];
            element.Value = value;
            element.Flags |= ScriptStackFlags.Initialized;
            count++;
        }
        while (TryWord(","u8));

        ExpectToken("}"u8);

        return count;
    }

    private void ParseArrayInitializer(int size, bool initializeAtRunTime)
    {
        ExpectToken("{"u8);
        int element = 0;
        for (; element < size; element++)
        {
            if (initializeAtRunTime)
            {
                EmitInitializer(_stack.Count - size + element);
                if (!TryWord(","u8))
                {
                    ExpectToken("}"u8);
                    break;
                }

                continue;
            }

            int value = ParseConstInt();
            ref var variable = ref _stack[_stack.Count - size + element];
            variable.Flags = (variable.Flags & ~ScriptStackFlags.NotInitialized) | ScriptStackFlags.Initialized;
            variable.Value = value;
            if (!TryWord(","u8))
            {
                ExpectToken("}"u8);
                break;
            }
        }

        if (element >= size)
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
            if (_text[_position] == '*')
            {
                _position++;
            }
        }

        var name = ReadName();
        if (_objects.Location(name) >= 0)
            throw ReportError(10, $"string redifinition '{Decode(name)}'");

        byte[] symbolName = name.ToArray();
        var flags = ScriptStackFlags.None;
        int size = 1;
        if (TryWord("["u8))
        {
            flags = ScriptStackFlags.ArrayElement;
            size = IsAlienShooter2 && _text[_position] == ']' ? UnsizedArray : ParseConstInt();
            ExpectToken("]"u8);
        }

        int symbol = _objects.Insert(symbolName, new ScriptObject(ScriptObjectKind.Variable, _stack.Count));
        Span<byte> literal = stackalloc byte[MaxNameLength + 1];
        int count = 0;
        if (TryWord("="u8))
        {
            bool braced = IsAlienShooter2 && flags.HasFlag(ScriptStackFlags.ArrayElement);
            if (braced)
            {
                ExpectToken("{"u8);
            }

            for (; !IsAlienShooter2 && count < size - 1; count++)
            {
                _stack.Insert(new ScriptStack(ScriptStackFlags.Text | flags, 0));
            }

            do
            {
                if (count >= size)
                    throw ReportError(10, "too many initializers");

                if (initializeAtRunTime)
                {
                    _stack.Insert(new ScriptStack(ScriptStackFlags.Text | flags | ScriptStackFlags.NotInitialized, 0));
                    EmitInitializer(_stack.Count - 1);
                }
                else
                {
                    byte[] text;
                    if (IsAlienShooter2)
                    {
                        text = ParseConstText();
                    }
                    else
                    {
                        int length = PutString(literal);
                        text = length > 0 ? literal[..(length - 1)].ToArray() : [];
                    }

                    _stack.Insert(new ScriptStack(ScriptStackFlags.Text | flags | ScriptStackFlags.Initialized, 0, text));
                }

                count++;
            }
            while (braced && TryWord(","u8));

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
            _stack.Insert(new ScriptStack(ScriptStackFlags.Text | flags, 0));
        }

        _objects[symbol].Count = size;
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


    [EngineRoutine(0x00422030)]
    private void ParseExpressionList()
    {
        do
        {
            ParseExpression();
            Emit(ScriptOpcode.EndStatement, IsAlienShooter2 ? _line + 1 : _line);
        }
        while (TryWord(","u8));
    }

    [EngineRoutine(0x00422080)]
    private void ParseStatement(List<int>? breaks)
    {
        bool once = TryWord("iff"u8);
        if (once || TryWord("if"u8))
        {
            ParseIf(once, breaks);
        }
        else if (TryWord("while"u8))
        {
            ParseWhile();
        }
        else if (TryWord("do"u8))
        {
            ParseDo();
        }
        else if (TryWord("for"u8))
        {
            ParseFor();
        }
        else if (TryWord("break"u8))
        {
            ParseBreak(breaks);
        }
        else if (TryWord("goto"u8))
        {
            ParseGoto();
        }
        else if (TryWord("{"u8))
        {
            ParseBlock(breaks);
        }
        else
        {
            ParseExpressionList();
            ExpectToken(";"u8);
        }
    }

    private void ParseIf(bool once, List<int>? breaks)
    {
        ExpectToken("("u8);
        ParseExpression();
        ExpectToken(")"u8);
        Emit(once ? ScriptOpcode.JumpIfFalseOnce : ScriptOpcode.JumpIfFalse);
        int skip = ReserveInt();
        ParseStatement(breaks);
        PatchHere(skip);
        if (TryWord("else"u8))
        {
            WriteCode(skip, ReadCode(skip) + 5);
            Emit(ScriptOpcode.Jump);
            int end = ReserveInt();
            ParseStatement(breaks);
            PatchHere(end);
        }
    }

    private void ParseWhile()
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

    private void ParseDo()
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

    private void ParseFor()
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

    private void PatchBreaks(List<int> breaks)
    {
        foreach (int address in breaks)
        {
            PatchHere(address);
        }
    }

    private void ParseBreak(List<int>? breaks)
    {
        ExpectToken(";"u8);
        if (breaks is null)
            throw ReportError(10, "'break' without loop");

        if (breaks.Count >= MaxBreaks)
            throw ReportError(10, "Too many 'break'");

        Emit(ScriptOpcode.Jump);
        breaks.Add(ReserveInt());
    }

    private void ParseGoto()
    {
        var name = ReadName();
        int found = _objects.Location(name);
        if (found < 0)
        {
            found = _objects.Insert(name.ToArray(), new ScriptObject(ScriptObjectKind.PendingLabel, _codeSize + 1));
        }
        else if (_objects[found].Kind == ScriptObjectKind.PendingLabel)
            throw ReportError(10, $"second use undefined label '{Decode(name)}'");
        else if (_objects[found].Kind != ScriptObjectKind.Label)
            throw ReportError(10, $"'{Decode(name)}' is not label");

        EmitJumpBack(ScriptOpcode.Jump, _objects[found].Address);
    }

    private void ParseBlock(List<int>? breaks)
    {
        int saved = _objects.Count;
        while (!TryWord("}"u8))
        {
            ParseStatement(breaks);
        }

        TruncateObjects(saved);
    }

    private void TruncateObjects(int count)
    {
        if (count < 1)
        {
            _objects.Count = 0;
        }
        else if (count < _objects.Count)
        {
            _objects.Count = count;
        }
    }

    [EngineRoutine(0x00422760)]
    private bool ParseTopLevelItem()
    {
        if (SkipBlanksAndDirectives())
            return true;

        var text = _text.AsSpan(_position);
        if (text.StartsWith("#define"u8))
        {
            ParseDefine();
        }
        else if (text.StartsWith("#undef"u8))
        {
            ParseUndef();
        }
        else if (TryWord("#include"u8))
        {
            ParseInclude();
        }
        else if (TryWord("extern"u8))
        {
            ParseExtern();
        }
        else if (TryWord("static"u8))
        {
            ParseStaticDeclarators();
            ExpectToken(";"u8);
        }
        else if (TryWord("int"u8))
        {
            ParseIntDeclarators(false);
            ExpectToken(";"u8);
        }
        else if (TryWord("string"u8))
        {
            ParseStringDeclarators(false);
            ExpectToken(";"u8);
        }
        else
        {
            ParseFunction();
        }

        return SkipBlanksAndDirectives();
    }

    private void ParseDefine()
    {
        _position += 7;
        _isSubstitutionOff = true;
        byte[] name = ReadName().ToArray();
        _isSubstitutionOff = false;
        var value = GetLine();
        int found = _defines.Location(name);
        if (found >= 0)
        {
            _defines[found] = value;
        }
        else
        {
            _defines.Insert(name, value);
        }
    }

    private void ParseUndef()
    {
        _position += 6;
        _isSubstitutionOff = true;
        int found = _defines.Location(ReadName());
        _isSubstitutionOff = false;
        if (found < 0)
            throw ReportError(4, "#undef parameters");

        for (int i = found; i < _defines.Count - 1; i++)
        {
            _defines.Entry(i) = _defines.Entry(i + 1);
        }

        _defines.Count--;
        if (_defines.Count == 0)
        {
            _defines.Release();
        }
    }

    private void ParseInclude()
    {
        string includer = FileName;
        if (_text[_position] != '"' && _text[_position] != '<')
            throw ReportError(13, "include file name");

        int start = ++_position;
        while (_text[_position] != '"' && _text[_position] != '>')
        {
            if (_position >= _end)
                throw ReportError(10, "End of file");

            _position++;
        }

        string path = Decode(_text.AsSpan(start, _position++ - start));
        if (!_fileSystem!.TryRead(path, out var file))
            throw ReportError(7, path);

        (byte[] saved, int position, int end, int line, int depth) = (_text, _position, _end, _line, _ifDepth);
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
            (_text, _position, _end, _line, _ifDepth) = (saved, position, end, line, depth);
            FileName = includer;
        }
    }

    private void ParseExtern()
    {
        int saved = _objects.Count;
        byte[] name = ReadName().ToArray();
        if (_objects.Location(name) >= 0)
            throw ReportError(10, "function redefinition");

        int firstVariable = _stack.Count;
        ExpectToken("("u8);
        do
        {
            if (TryWord("int"u8))
            {
                IntVar();
            }
            else if (TryWord("string"u8))
            {
                ParseStringDeclarator();
            }
        }
        while (TryWord(","u8));

        ExpectToken(")"u8);
        int id = ParseConstInt();
        if (!IsDigit(_text[_position - 1]))
            throw ReportError(13, "extern function code");

        TruncateObjects(saved);
        _objects.Insert(name, new ScriptObject(ScriptObjectKind.Extern, id, firstVariable, _stack.Count - firstVariable));
        ExpectToken(";"u8);
    }

    private void ParseFunction()
    {
        int objectCount = _objects.Count;
        byte[] functionName = ReadName().ToArray();
        int prototype = _objects.Location(functionName);
        if (prototype >= 0 && !IsAlienShooter2)
            throw ReportError(10, "function redefinition");

        int codeStart = _codeSize;
        int firstParameter = _stack.Count;
        ExpectToken("("u8);
        int parameter = 0;
        do
        {
            if (TryWord("int"u8))
            {
                IntVar();
            }
            else if (TryWord("string"u8))
            {
                ParseStringDeclarator();
            }
            else
                continue;

            if (prototype >= 0)
            {
                _objects[_objects.Count - 1].Address = _objects[prototype].FirstVariable + parameter;
            }

            parameter++;
        }
        while (TryWord(","u8));

        ExpectToken(")"u8);
        int parameterCount = _stack.Count - firstParameter;
        if (prototype >= 0)
        {
            if (_objects[prototype].Kind != ScriptObjectKind.Function || _objects[prototype].Count != parameterCount || _objects[prototype].Address != -1)
                throw ReportError(10, "function redefinition");

            _stack.Count = firstParameter;
        }

        int address = -1;
        if (!IsAlienShooter2 || !TryWord(";"u8))
        {
            address = codeStart;
            if (prototype >= 0)
            {
                _objects[prototype].Address = codeStart;
            }

            ExpectToken("{"u8);
            while (!TryWord("}"u8))
            {
                ParseStatement(null);
            }

            Emit(ScriptOpcode.Return);
        }

        TruncateObjects(objectCount);
        if (prototype < 0)
        {
            int index = _objects.Insert(functionName, new ScriptObject(ScriptObjectKind.Function, address, firstParameter, parameterCount));
            if (functionName.AsSpan().SequenceEqual("main"u8))
            {
                MainIndex = index;
            }
            else if (IsAlienShooter2 && IsScriptEvent(functionName) && parameterCount != 3)
                throw ReportError(10, "ScriptEvent must have 3 parameters");
        }
    }
}
