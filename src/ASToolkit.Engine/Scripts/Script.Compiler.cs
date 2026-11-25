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
        Release();
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
    internal void Release()
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

    [EngineRoutine(0x0041f450)]
    private void LoadLgc(IGameFileSystem fileSystem, string path, ReadOnlySpan<byte> file)
    {
        Release();
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
                Release();
            }
            else
            {
                _code = _codeBuffer.AsSpan(0, _codeSize).ToArray();
            }
        }
        catch (InvalidDataException ex) when (IsAlienShooter2)
        {
            Release();
            CompileError = ex.Message;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(_codeBuffer);
            _codeBuffer = [];
            _fileSystem = null;
        }
    }

    private bool IsAlphanumeric(byte character) => IsAlienShooter2 ? char.IsAsciiLetterOrDigit((char)character) : CRuntime.IsAlphanumeric(character);

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
            if (mode != 0)
            {
                if (mode == 1 ? _text[_position] == '\n' : _text[_position] == '/' && _text[_position - 1] == '*')
                {
                    mode = 0;
                }

                if (_text[_position++] == '\n')
                {
                    _line++;
                }

                continue;
            }

            byte character = _text[_position];
            bool isSubstituted = (IsAlpha(character) || character == '_') && !_isSubstitutionOff;
            int length = 0;
            while (isSubstituted && IsNameCharacter(_text[_position + length]))
            {
                if (length >= MaxNameLength)
                    throw ReportError(10, "Very long name");

                length++;
            }

            int found = isSubstituted ? _defines.Location(_text.AsSpan(_position, length)) : -1;
            if (found >= 0)
            {
                var value = _defines[found].Span;
                _position = _position + length - value.Length;
                value.CopyTo(_text.AsSpan(_position));
            }

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
                if (skip == 0 && _ifDepth > 0)
                {
                    skip = _ifDepth;
                }
                else if (skip == _ifDepth)
                {
                    skip = 0;
                }

                if (_ifDepth < 1)
                {
                    LogScriptError(10, "#else without #ifdef");
                }
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

                if (skip == 0 && !(IsAlienShooter2 ? CRuntime.IsWhiteSpace(character) : CRuntime.IsSpace(character)) && character != 0)
                    return false;
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

    [EngineRoutine(0x004202a0)]
    private void SkipEmpty()
    {
        if (SkipBlanksAndDirectives())
            throw ReportError(10, "End of file");
    }

    [EngineRoutine(0x004202d0)]
    private byte[] GetLine()
    {
        SkipEmpty();
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
        SkipEmpty();

        return value;
    }

    [EngineRoutine(0x004203b0)]
    private ReadOnlySpan<byte> ReadName()
    {
        SkipEmpty();
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
        SkipEmpty();

        return _name.AsSpan(0, _nameLength);
    }

    [EngineRoutine(0x00420460)]
    private bool TryWord(ReadOnlySpan<byte> token)
    {
        SkipEmpty();
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

    private bool IsNameCharacter(byte character) => IsAlphanumeric(character) || character == '_';

    [EngineRoutine(0x004204f0)]
    private void WordEnd(ReadOnlySpan<byte> token)
    {
        if (!TryWord(token))
            throw ReportError(13, Decode(token));
    }

    [EngineRoutine(0x00420520)]
    private int GetInt()
    {
        SkipEmpty();
        int start = _codeSize;
        ParseExpression();
        if (_codeBuffer[start] != (byte)ScriptOpcode.PushNumber || _codeSize - start != 5)
            throw ReportError(4, "constant int value");

        _codeSize -= 5;

        return ReadCode(start + 1);
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
            byte next = _text[_position + 1];
            if (character != '\\')
            {
                output[count++] = character;
            }
            else if (next == '\r' && _text[_position + 2] == '\n')
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

            character = _text[++_position];
        }

        if (_end <= _position++)
            throw ReportError(10, "End of file");

        output[count++] = 0;

        return count;
    }

    private void AddDataCommand(byte value) => _codeBuffer[_codeSize++] = value;

    private void AddDataCommand(ScriptOpcode opcode) => AddDataCommand((byte)opcode);

    private void EmitInt(int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(_codeBuffer.AsSpan(_codeSize), value);
        _codeSize += sizeof(int);
    }

    private void AddDataCommand(ScriptOpcode opcode, int operand)
    {
        AddDataCommand(opcode);
        EmitInt(operand);
    }

    private void WriteCode(int address, int value) => BinaryPrimitives.WriteInt32LittleEndian(_codeBuffer.AsSpan(address), value);

    [EngineRoutine(0x00420700)]
    private void SetNoElement(int count) => _objects[_objects.Count - 1].Count = count;

    [EngineRoutine(0x00420720)]
    private void IntVar(bool initializeAtRunTime = false)
    {
        var flags = ScriptStackFlags.None;
        SkipEmpty();
        if (_text[_position] == '*')
        {
            flags = ScriptStackFlags.Pointer;
            _position++;
        }

        var name = ReadName();
        if (_objects.Location(name) >= 0)
            throw ReportError(10, $"int redefinition '{Decode(name)}'");

        _objects.Insert(name.ToArray(), new ScriptObject(ScriptObjectKind.Variable, _stack.Count));
        int size = 1;
        bool isArray = TryWord("["u8);
        if (isArray)
        {
            flags |= ScriptStackFlags.ArrayElement;
        }

        if (isArray && _text[_position] == ']')
        {
            _position++;
            if (!TryWord("="u8))
                throw ReportError(10, "for [] need initialisation");

            WordEnd("{"u8);
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
                int value = GetInt();
                ref var element = ref _stack[_stack.Count - 1];
                element.Value = value;
                element.Flags |= ScriptStackFlags.Initialized;
                count++;
            }
            while (TryWord(","u8));

            WordEnd("}"u8);
            SetNoElement(count);
            return;
        }

        if (isArray)
        {
            size = GetInt();
            WordEnd("]"u8);
        }

        for (int element = 0; element < size; element++)
        {
            _stack.Insert(new ScriptStack(ScriptStackFlags.Number | flags | ScriptStackFlags.NotInitialized, 0));
        }

        bool isInitialized = TryWord("="u8);
        if (isInitialized && flags.HasFlag(ScriptStackFlags.ArrayElement))
        {
            WordEnd("{"u8);
            int element = 0;
            for (; element < size; element++)
            {
                if (initializeAtRunTime)
                {
                    EmitInitializer(_stack.Count - size + element);
                }
                else
                {
                    int value = GetInt();
                    ref var variable = ref _stack[_stack.Count - size + element];
                    variable.Flags = (variable.Flags & ~ScriptStackFlags.NotInitialized) | ScriptStackFlags.Initialized;
                    variable.Value = value;
                }

                if (!TryWord(","u8))
                {
                    WordEnd("}"u8);
                    break;
                }
            }

            if (element >= size)
                throw ReportError(10, "too many initializers");
        }
        else if (isInitialized && initializeAtRunTime)
        {
            EmitInitializer(_stack.Count - 1);
        }
        else if (isInitialized)
        {
            int value = GetInt();
            ref var variable = ref _stack[_stack.Count - 1];
            variable.Value = value;
            variable.Flags = (variable.Flags | ScriptStackFlags.Initialized) & ~ScriptStackFlags.NotInitialized;
        }

        SetNoElement(size);
    }

    [EngineRoutine(0x00420aa0)]
    private void ParseStringDeclarator(bool initializeAtRunTime = false)
    {
        if (IsAlienShooter2)
        {
            SkipEmpty();
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
            size = IsAlienShooter2 && _text[_position] == ']' ? UnsizedArray : GetInt();
            WordEnd("]"u8);
        }

        _objects.Insert(symbolName, new ScriptObject(ScriptObjectKind.Variable, _stack.Count));
        Span<byte> literal = stackalloc byte[MaxNameLength + 1];
        int count = 0;
        if (TryWord("="u8))
        {
            bool braced = IsAlienShooter2 && flags.HasFlag(ScriptStackFlags.ArrayElement);
            if (braced)
            {
                WordEnd("{"u8);
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
                    count++;
                    continue;
                }

                byte[] text;
                if (IsAlienShooter2)
                {
                    text = GetString();
                }
                else
                {
                    int length = PutString(literal);
                    text = length > 0 ? literal[..(length - 1)].ToArray() : [];
                }

                _stack.Insert(new ScriptStack(ScriptStackFlags.Text | flags | ScriptStackFlags.Initialized, 0, text));
                count++;
            }
            while (braced && TryWord(","u8));

            if (braced)
            {
                WordEnd("}"u8);
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

        SetNoElement(size);
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

            var unary = ScriptOpcode.None;
            if (IsAlienShooter2 ? TryWord("-"u8) : _text[_position + 1] != '-' && TryWord("-"u8))
            {
                unary = ScriptOpcode.Negate;
            }
            else if (IsAlienShooter2 && TryWord("+"u8))
            {
                unary = ScriptOpcode.None;
            }
            else if (TryWord("~"u8))
            {
                unary = ScriptOpcode.BitwiseNot;
            }
            else if (TryWord("!"u8))
            {
                unary = ScriptOpcode.LogicalNot;
            }

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
                AddDataCommand(ScriptOpcode.PushNumber, value);
                if (cast != ScriptOpcode.None)
                {
                    AddDataCommand(cast);
                }

                return;
            }

            if (character == '"')
            {
                AddDataCommand(ScriptOpcode.PushText);
                _codeSize += PutString(_codeBuffer.AsSpan(_codeSize));
            }
            else if (character == '\'')
            {
                AddDataCommand(ScriptOpcode.PushNumber);
                _position++;
                EmitInt((sbyte)_text[_position++]);
                if (_text[_position] != '\'')
                    throw ReportError(13, "second '");

                _position++;
            }
            else if (TryWord("sizeof"u8))
            {
                if (!TryWord("("u8))
                    throw ReportError(13, "'(' for sizeof");

                AddDataCommand(ScriptOpcode.PushNumber);
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

                WordEnd(")"u8);
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
                AddDataCommand(ScriptOpcode.Return);
            }
            else if (TryWord("("u8))
            {
                ParseExpression();
                WordEnd(")"u8);
            }
            else if (IsAlpha(_text[_position]))
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
                    continue;
                }

                switch (_objects[found].Kind)
                {
                    case ScriptObjectKind.Variable:
                        int variable = _objects[found].Address;
                        int indexLength = 0;
                        byte[]? index = null;
                        bool hasIndex = TryWord("["u8);
                        if (hasIndex && !_stack[variable].Flags.HasFlag(ScriptStackFlags.Text) && !_stack[variable].Flags.HasFlag(ScriptStackFlags.ArrayElement) && !_stack[variable].Flags.HasFlag(ScriptStackFlags.Pointer))
                            throw ReportError(10, "[] for not array");

                        if (hasIndex)
                        {
                            int indexStart = _codeSize;
                            ParseExpression();
                            AddDataCommand(ScriptOpcode.SetIndex);
                            indexLength = _codeSize - indexStart;
                            index = ArrayPool<byte>.Shared.Rent(indexLength);
                            _codeBuffer.AsSpan(indexStart, indexLength).CopyTo(index);
                            _codeSize = indexStart;
                            WordEnd("]"u8);
                        }

                        var opcode = access;
                        var compound = ScriptOpcode.None;
                        if (_text[_position + 1] != '=' && TryWord("="u8))
                        {
                            ParseExpression();
                            opcode = ScriptOpcode.Assign;
                        }
                        else if (TryWord("+="u8))
                        {
                            compound = ScriptOpcode.Add;
                        }
                        else if (TryWord("-="u8))
                        {
                            compound = ScriptOpcode.Subtract;
                        }
                        else if (TryWord("/="u8))
                        {
                            compound = ScriptOpcode.Divide;
                        }
                        else if (TryWord("*="u8))
                        {
                            compound = ScriptOpcode.Multiply;
                        }
                        else if (TryWord("%="u8))
                        {
                            compound = ScriptOpcode.Modulo;
                        }
                        else if (TryWord("&="u8))
                        {
                            compound = ScriptOpcode.And;
                        }
                        else if (TryWord("|="u8))
                        {
                            compound = ScriptOpcode.Or;
                        }
                        else if (TryWord("^="u8))
                        {
                            compound = ScriptOpcode.Xor;
                        }
                        else if (TryWord("<<="u8))
                        {
                            compound = ScriptOpcode.ShiftLeft;
                        }
                        else if (TryWord(">>="u8))
                        {
                            compound = ScriptOpcode.ShiftRight;
                        }

                        if (compound != ScriptOpcode.None)
                        {
                            ParseExpression();
                            opcode = ScriptOpcode.CompoundAssign;
                        }
                        else if (opcode != ScriptOpcode.Assign && TryWord("++"u8))
                        {
                            opcode = ScriptOpcode.PostIncrement;
                        }
                        else if (opcode != ScriptOpcode.Assign && TryWord("--"u8))
                        {
                            opcode = ScriptOpcode.PostDecrement;
                        }

                        bool isReference = _stack[variable].Flags.HasFlag(ScriptStackFlags.ArrayElement) || _stack[variable].Flags.HasFlag(ScriptStackFlags.Pointer);
                        if (index is not null)
                        {
                            index.AsSpan(0, indexLength).CopyTo(_codeBuffer.AsSpan(_codeSize));
                            _codeSize += indexLength;
                            ArrayPool<byte>.Shared.Return(index);
                        }
                        else if (isReference && opcode is >= ScriptOpcode.PostIncrement and <= ScriptOpcode.PreDecrement)
                        {
                            throw ReportError(10, "Increment or decrement for array");
                        }
                        else if (isReference && opcode != ScriptOpcode.Load)
                        {
                            throw ReportError(4, "operation for array", (int)opcode);
                        }

                        AddDataCommand(opcode, variable);
                        if (opcode == ScriptOpcode.CompoundAssign)
                        {
                            AddDataCommand(compound);
                        }
                        break;
                    case ScriptObjectKind.Extern:
                        var externTarget = _objects[found];
                        WordEnd("("u8);
                        int externCount = 0;
                        while (!TryWord(")"u8))
                        {
                            ParseExpression();
                            TryWord(","u8);
                            externCount++;
                        }

                        for (; externCount < externTarget.Count && _stack[externTarget.FirstVariable + externCount].Flags.HasFlag(ScriptStackFlags.Initialized); externCount++)
                        {
                            AddDataCommand(ScriptOpcode.Load, externTarget.FirstVariable + externCount);
                        }

                        if (externCount != externTarget.Count)
                            throw ReportError(4, "extern function parameters number");

                        AddDataCommand((byte)externTarget.Address);
                        break;
                    case ScriptObjectKind.Function:
                        var callee = _objects[found];
                        WordEnd("("u8);
                        int argumentCount = 0;
                        while (!TryWord(")"u8))
                        {
                            ParseExpression();
                            TryWord(","u8);
                            AddDataCommand(ScriptOpcode.Assign, callee.FirstVariable + argumentCount++);
                            AddDataCommand(ScriptOpcode.Pop);
                        }

                        for (; argumentCount < callee.Count && _stack[callee.FirstVariable + argumentCount].Flags.HasFlag(ScriptStackFlags.Initialized); argumentCount++)
                        {
                            ref readonly var parameter = ref _stack[callee.FirstVariable + argumentCount];
                            if (!parameter.Flags.HasFlag(ScriptStackFlags.Text))
                            {
                                AddDataCommand(ScriptOpcode.PushNumber, parameter.Value);
                            }
                            else
                            {
                                EmitPushText(parameter.Text.Span);
                            }

                            AddDataCommand(ScriptOpcode.Assign, callee.FirstVariable + argumentCount);
                            AddDataCommand(ScriptOpcode.Pop);
                        }

                        if (argumentCount != callee.Count)
                            throw ReportError(4, "function parameters number");

                        AddDataCommand(ScriptOpcode.Call, IsAlienShooter2 ? found : callee.Address);
                        break;
                    case ScriptObjectKind.Text:
                        EmitPushText(_objects[found].Text.Span);
                        break;
                    case ScriptObjectKind.Number:
                        AddDataCommand(ScriptOpcode.PushNumber, _objects[found].Address);
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
                        continue;
                }
            }
            else
            {
                if (unary != ScriptOpcode.None || _text[_position] == 0 || ".$#@`"u8.Contains(_text[_position]))
                    throw ReportError(10, "error symbol");

                return;
            }

            if (unary != ScriptOpcode.None)
            {
                AddDataCommand(unary);
            }

            if (cast != ScriptOpcode.None)
            {
                AddDataCommand(cast);
            }

            return;
        }
    }

    private bool IsAlpha(byte character) => IsAlienShooter2 ? char.IsAsciiLetter((char)character) : CRuntime.IsAlpha(character);

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

    private void EmitPushText(ReadOnlySpan<byte> text)
    {
        AddDataCommand(ScriptOpcode.PushText);
        text.CopyTo(_codeBuffer.AsSpan(_codeSize));
        _codeSize += text.Length;
        AddDataCommand((byte)0);
    }

    [EngineRoutine(0x00421b70)]
    private void SetOperation(int start, ScriptOpcode opcode)
    {
        if (_codeBuffer[start] != (byte)ScriptOpcode.PushNumber || _codeBuffer[start + 5] != (byte)ScriptOpcode.PushNumber || _codeSize - start != 10)
        {
            if (!IsAlienShooter2 || opcode != ScriptOpcode.Add || !TryFoldTexts(start))
            {
                AddDataCommand(opcode);
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
                AddDataCommand(ScriptOpcode.GreaterOrEqual);
            }
            else if (TryWord(">>"u8))
            {
                ParseAdditive();
                SetOperation(start, ScriptOpcode.ShiftRight);
            }
            else if (TryWord(">"u8))
            {
                ParseAdditive();
                AddDataCommand(ScriptOpcode.Greater);
            }
            else if (TryWord("<="u8))
            {
                ParseAdditive();
                AddDataCommand(ScriptOpcode.LessOrEqual);
            }
            else if (TryWord("<<"u8))
            {
                ParseAdditive();
                SetOperation(start, ScriptOpcode.ShiftLeft);
            }
            else if (TryWord("<"u8))
            {
                ParseAdditive();
                AddDataCommand(ScriptOpcode.Less);
            }
            else if (TryWord("=="u8))
            {
                ParseAdditive();
                AddDataCommand(ScriptOpcode.Equal);
            }
            else if (TryWord("!="u8))
            {
                ParseAdditive();
                AddDataCommand(ScriptOpcode.NotEqual);
            }
            else
                break;
        }
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
                AddDataCommand(ScriptOpcode.LogicalAnd);
            }
            else if (TryWord("&"u8))
            {
                ParseComparison();
                SetOperation(start, ScriptOpcode.And);
            }
            else if (TryWord("||"u8))
            {
                ParseComparison();
                AddDataCommand(ScriptOpcode.LogicalOr);
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
            AddDataCommand(ScriptOpcode.OrJump);
            int skip = ReserveInt();
            ParseLogicalAnd();
            AddDataCommand(ScriptOpcode.LogicalOr);
            PatchHere(skip);
        }
    }

    private void ParseLogicalAnd()
    {
        ParseBitwiseOr();
        while (TryWord("&&"u8))
        {
            AddDataCommand(ScriptOpcode.AndJump);
            int skip = ReserveInt();
            ParseBitwiseOr();
            AddDataCommand(ScriptOpcode.LogicalAnd);
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

    private void EmitInitializer(int variable)
    {
        ParseExpression();
        AddDataCommand(ScriptOpcode.Assign, variable);
        AddDataCommand(ScriptOpcode.EndStatement, _line);
    }

    [EngineRoutine(0x00422030)]
    private void ParseExpressionList()
    {
        do
        {
            ParseExpression();
            AddDataCommand(ScriptOpcode.EndStatement, IsAlienShooter2 ? _line + 1 : _line);
        }
        while (TryWord(","u8));
    }

    private byte[] GetString()
    {
        SkipEmpty();
        int start = _codeSize;
        ParseExpression();
        int length = _codeBuffer[start] == (byte)ScriptOpcode.PushText ? _codeBuffer.AsSpan(start + 1, _codeSize - start - 1).IndexOf((byte)0) : -1;
        if (length < 0 || _codeSize - start != length + 2)
            throw ReportError(4, "constant string value");

        _codeSize = start;

        return _codeBuffer.AsSpan(start + 1, length).ToArray();
    }

    [EngineRoutine(0x00422080)]
    private void ParseStatement(List<int>? breaks)
    {
        bool once = TryWord("iff"u8);
        if (once || TryWord("if"u8))
        {
            WordEnd("("u8);
            ParseExpression();
            WordEnd(")"u8);
            AddDataCommand(once ? ScriptOpcode.JumpIfFalseOnce : ScriptOpcode.JumpIfFalse);
            int skip = ReserveInt();
            ParseStatement(breaks);
            PatchHere(skip);
            if (TryWord("else"u8))
            {
                WriteCode(skip, ReadCode(skip) + 5);
                AddDataCommand(ScriptOpcode.Jump);
                int end = ReserveInt();
                ParseStatement(breaks);
                PatchHere(end);
            }
        }
        else if (TryWord("while"u8))
        {
            var loop = new List<int>();
            WordEnd("("u8);
            int start = _codeSize;
            ParseExpression();
            WordEnd(")"u8);
            AddDataCommand(ScriptOpcode.JumpIfFalse);
            int exit = ReserveInt();
            ParseStatement(loop);
            EmitJumpBack(ScriptOpcode.Jump, start);
            PatchHere(exit);
            PatchBreaks(loop);
        }
        else if (TryWord("do"u8))
        {
            var loop = new List<int>();
            int start = _codeSize;
            ParseStatement(loop);
            WordEnd("while"u8);
            WordEnd("("u8);
            ParseExpression();
            WordEnd(")"u8);
            AddDataCommand(ScriptOpcode.LogicalNot);
            EmitJumpBack(ScriptOpcode.JumpIfFalse, start);
            PatchBreaks(loop);
        }
        else if (TryWord("for"u8))
        {
            var loop = new List<int>();
            WordEnd("("u8);
            ParseExpressionList();
            WordEnd(";"u8);
            int condition = _codeSize;
            ParseExpression();
            WordEnd(";"u8);
            AddDataCommand(ScriptOpcode.JumpIfFalse);
            int exit = ReserveInt();
            AddDataCommand(ScriptOpcode.Jump);
            int body = ReserveInt();
            int step = _codeSize;
            ParseExpressionList();
            WordEnd(")"u8);
            EmitJumpBack(ScriptOpcode.Jump, condition);
            PatchHere(body);
            ParseStatement(loop);
            EmitJumpBack(ScriptOpcode.Jump, step);
            PatchHere(exit);
            PatchBreaks(loop);
        }
        else if (TryWord("break"u8))
        {
            WordEnd(";"u8);
            if (breaks is null)
                throw ReportError(10, "'break' without loop");

            if (breaks.Count >= MaxBreaks)
                throw ReportError(10, "Too many 'break'");

            AddDataCommand(ScriptOpcode.Jump);
            breaks.Add(ReserveInt());
        }
        else if (TryWord("goto"u8))
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
        else if (TryWord("{"u8))
        {
            int saved = _objects.Count;
            while (!TryWord("}"u8))
            {
                ParseStatement(breaks);
            }

            TruncateObjects(saved);
        }
        else
        {
            ParseExpressionList();
            WordEnd(";"u8);
        }
    }

    private int ReadCode(int address) => BinaryPrimitives.ReadInt32LittleEndian(_codeBuffer.AsSpan(address));

    private int ReserveInt()
    {
        int address = _codeSize;
        _codeSize += sizeof(int);

        return address;
    }

    private void EmitJumpBack(ScriptOpcode opcode, int target)
    {
        AddDataCommand(opcode);
        WriteCode(_codeSize, target - _codeSize);
        _codeSize += sizeof(int);
    }

    private void PatchBreaks(List<int> breaks)
    {
        foreach (int address in breaks)
        {
            PatchHere(address);
        }
    }

    private void PatchHere(int address) => WriteCode(address, _codeSize - address);

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
            byte[] value = GetLine();
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
        else if (text.StartsWith("#undef"u8))
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
        else if (TryWord("#include"u8))
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
        else if (TryWord("extern"u8))
        {
            int saved = _objects.Count;
            byte[] name = ReadName().ToArray();
            if (_objects.Location(name) >= 0)
                throw ReportError(10, "function redefinition");

            int firstVariable = _stack.Count;
            WordEnd("("u8);
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

            WordEnd(")"u8);
            int id = GetInt();
            if (!IsDigit(_text[_position - 1]))
                throw ReportError(13, "extern function code");

            TruncateObjects(saved);
            _objects.Insert(name, new ScriptObject(ScriptObjectKind.Extern, id, firstVariable, _stack.Count - firstVariable));
            WordEnd(";"u8);
        }
        else if (TryWord("static"u8))
        {
            ParseStaticDeclarators();
            WordEnd(";"u8);
        }
        else if (TryWord("int"u8))
        {
            ParseIntDeclarators(false);
            WordEnd(";"u8);
        }
        else if (TryWord("string"u8))
        {
            ParseStringDeclarators(false);
            WordEnd(";"u8);
        }
        else
        {
            int objectCount = _objects.Count;
            byte[] functionName = ReadName().ToArray();
            int prototype = _objects.Location(functionName);
            if (prototype >= 0 && !IsAlienShooter2)
                throw ReportError(10, "function redefinition");

            int codeStart = _codeSize;
            int firstParameter = _stack.Count;
            WordEnd("("u8);
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

            WordEnd(")"u8);
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

                WordEnd("{"u8);
                while (!TryWord("}"u8))
                {
                    ParseStatement(null);
                }

                AddDataCommand(ScriptOpcode.Return);
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

        return SkipBlanksAndDirectives();
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

    private void OpenText(ReadOnlySpan<byte> file)
    {
        _text = ArrayPool<byte>.Shared.Rent(file.Length + TextExtra);
        _text.AsSpan(0, file.Length + TextExtra).Clear();
        file.CopyTo(_text.AsSpan(TextPrefix));
        _position = TextPrefix;
        _end = TextPrefix + file.Length;
    }

    private bool IsDigit(byte character) => IsAlienShooter2 ? char.IsAsciiDigit((char)character) : CRuntime.IsDigit(character);

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

    [EngineRoutine(0x00424f70)]
    private InvalidDataException ReportError(int code, string text, int extra = 0)
    {
        LogScriptError(code, text, extra);

        return new InvalidDataException($"'{FileName}' line {_line + 1}: {MyError.FormatError((LogError)code, extra, GameTextEncoding.Cyrillic.GetBytes(text))}");
    }
}
