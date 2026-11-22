using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine.IO;

internal ref struct Resource
{
    public const int Found = 0;
    public const int NotFound = 2;
    private const int TagSize = 4;
    private const int ChunkHeaderSize = 8;
    private const int FirstRecordOffset = 0x18;

    private ReadOnlySpan<byte> _data;
    private ReadOnlySpan<byte> _name;
    private bool _isOpen;
    private bool _isRes;
    private int _position;
    private int _chunkTag;
    private int _chunkSize;
    private int _chunkOffset;
    private int _start;
    private int _end;
    private int _recordOffset;
    private int _recordLength;

    public static ReadOnlySpan<byte> AnyTag => "ANY "u8;

    private static ReadOnlySpan<byte> ResSignature => "RES "u8;

    private static ReadOnlySpan<byte> RiffSignature => "RIFF"u8;

    private static ReadOnlySpan<byte> LogTag => "RES '%s' '%.4s'"u8;

    public static Resource FromRecord(ReadOnlySpan<byte> data) => new() { _data = data, _isOpen = true };

    [EngineRoutine(0x00407a40)]
    public int Open(IGameFileSystem fileSystem, GameLogger logger, ReadOnlySpan<byte> name, ReadOnlySpan<byte> kind)
    {
        _isOpen = false;
        if (!name.IsEmpty && fileSystem.TryRead(GameTextEncoding.Cyrillic.GetString(name), out var data))
        {
            _name = name;
            return Open(logger, data.Span, kind);
        }

        Log(logger, LogError.Open, 0, name);

        return 1;
    }

    [EngineRoutine(0x00407cf0)]
    public int FindChunk(ReadOnlySpan<byte> tag)
    {
        if (!_isOpen)
            return 1;

        _chunkOffset = _start + sizeof(int);
        _chunkSize = 0;

        return FindNextChunk(tag);
    }

    [EngineRoutine(0x00407d20)]
    public int FindNextChunk(ReadOnlySpan<byte> tag)
    {
        if (!_isOpen)
            return 1;

        int wanted = BinaryPrimitives.ReadInt32LittleEndian(tag);
        int any = BinaryPrimitives.ReadInt32LittleEndian(AnyTag);
        do
        {
            int chunkSpan = (_chunkSize + 1 & ~1) + ChunkHeaderSize;
            _chunkOffset += chunkSpan;
            _position = _chunkOffset;
            if (_end <= _chunkOffset)
            {
                _chunkOffset -= chunkSpan;
                _position = _isRes ? _chunkOffset + FirstRecordOffset : _chunkOffset + ChunkHeaderSize;

                return NotFound;
            }

            _chunkTag = Read<int>();
            _chunkSize = Read<int>();
            if (_isRes)
            {
                var flags = Read<ResChunkFlags>();
                if (flags.HasFlag(ResChunkFlags.ExtendedHeader))
                {
                    Read(sizeof(int) * 2);
                }

                _recordOffset = _position;
                _recordLength = Read<int>();
            }
        }
        while (wanted != _chunkTag && wanted != any);

        return Found;
    }

    [EngineRoutine(0x00407e70)]
    public int NextRecord(ReadOnlySpan<byte> tag)
    {
        if (!_isOpen)
            return -1;

        _recordOffset += _recordLength + sizeof(int);
        if (_chunkOffset + ChunkHeaderSize + _chunkSize <= _recordOffset)
            return FindNextChunk(tag);

        _position = _recordOffset;
        _recordLength = Read<int>();

        return Found;
    }

    [EngineRoutine(0x00407b90)]
    public ReadOnlySpan<byte> Read(int length)
    {
        if (!_isOpen || length <= 0)
            return [];

        int count = Math.Clamp(_data.Length - _position, 0, length);
        var read = _data.Slice(_position, count);
        _position += count;

        return read;
    }

    public T Read<T>()
        where T : unmanaged =>
        TryRead(out T value) ? value : throw new EndOfStreamException();

    public ReadOnlySpan<T> Read<T>(int count)
        where T : unmanaged =>
        MemoryMarshal.Cast<byte, T>(Read(count * Unsafe.SizeOf<T>()));

    public bool TryRead<T>(out T value)
        where T : unmanaged
    {
        var read = Read(Unsafe.SizeOf<T>());
        bool isComplete = read.Length == Unsafe.SizeOf<T>();
        value = isComplete ? MemoryMarshal.Read<T>(read) : default;

        return isComplete;
    }

    public void ReadInto<T>(ref T value)
        where T : unmanaged
    {
        if (TryRead(out T read))
        {
            value = read;
        }
    }

    public ReadOnlySpan<byte> ReadText() => ReadText(_data, ref _position);

    [EngineRoutine(0x00406590)]
    public static ReadOnlySpan<byte> ReadText(ReadOnlySpan<byte> data, scoped ref int position)
    {
        int start = Math.Min(position, data.Length);
        var rest = data[start..];
        int length = rest.IndexOfAny((byte)0, CRuntime.LineFeed);
        int consumed = length < 0 ? rest.Length : length + 1;
        var text = rest[..(length < 0 ? rest.Length : length)];
        position = start + consumed;

        if (!text.Contains(CRuntime.CarriageReturn))
            return text;

        byte[] kept = new byte[text.Length - text.Count(CRuntime.CarriageReturn)];
        int i = 0;
        foreach (byte character in text)
        {
            if (character != CRuntime.CarriageReturn)
            {
                kept[i++] = character;
            }
        }

        return kept;
    }

    [EngineRoutine(0x00407870)]
    private int Open(GameLogger logger, ReadOnlySpan<byte> data, ReadOnlySpan<byte> kind)
    {
        _data = data;
        _isOpen = true;
        _position = 0;
        _start = _position;
        var signature = Read(TagSize);
        if (signature.Length < TagSize)
        {
            Log(logger, LogError.Load, 0, "empty file"u8);
            _isOpen = false;
            return 4;
        }

        _isRes = signature.SequenceEqual(ResSignature);
        if (!_isRes && !signature.SequenceEqual(RiffSignature))
        {
            Log(logger, LogError.Invalid, 0, "resource signature"u8);
            _isOpen = false;
            return 2;
        }

        _end = Read<int>();
        _end += _start + ChunkHeaderSize;
        if (_data.Length < _end)
        {
            Log(logger, LogError.Text, _data.Length - _end, "Invalid filelength"u8);
        }

        var type = Read(TagSize);
        if (!type.SequenceEqual(kind) && !kind.SequenceEqual(AnyTag))
        {
            _chunkTag = BinaryPrimitives.ReadInt32LittleEndian(type);
            Log(logger, LogError.Invalid, 0, "resource type"u8);
            _isOpen = false;
            return 3;
        }

        FindChunk(AnyTag);

        return 0;
    }

    private readonly void Log(GameLogger logger, LogError code, int value, ReadOnlySpan<byte> text)
    {
        byte[] tag = new byte[TagSize];
        BinaryPrimitives.WriteInt32LittleEndian(tag, _chunkTag);
        logger.Error(LogTag, code, value, text, (ReadOnlyMemory<byte>)_name.ToArray(), (ReadOnlyMemory<byte>)tag);
    }
}
