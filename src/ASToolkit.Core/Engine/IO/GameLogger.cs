using System.Globalization;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine.IO;

internal sealed class GameLogger : IDisposable
{
    private const int BufferSize = 1024;
    private const string NamePrefix = "logs\\error";
    private const string FolderPrefix = "logs\\";
    private const string NameExtension = ".log";
    private const string LastPath = "logs\\error.log";
    private const string FallbackPath = "error.log";
    private const string DateFormat = "yyyy-MM-dd";
    private const string TimeFormat = "HH:mm:ss";
    private const string CreatedPrefix = "Cr-";
    private const string AccessedPrefix = " La-";
    private const string WrittenPrefix = " Lw-";

    private readonly IGameFileSystem? _fileSystem;
    private readonly TimeProvider _time;
    private readonly string _path;
    private Stream? _stream;

    [EngineRoutine(0x004084a0)]
    public GameLogger(IGameFileSystem? fileSystem, TimeProvider time, string program)
    {
        _fileSystem = fileSystem;
        _time = time;
        var now = time.GetLocalNow();
        string clock = now.ToString(TimeFormat, CultureInfo.InvariantCulture);
        string date = now.ToString(DateFormat, CultureInfo.InvariantCulture);
        string path = ReplaceFirst(ReplaceFirst($"{NamePrefix}{date} {clock}{NameExtension}", ':', 'h'), ':', 'm');
        _stream = fileSystem?.OpenWrite(path, false);

        if (_stream is null)
        {
            path = path[FolderPrefix.Length..];
            _stream = fileSystem?.OpenWrite(path, false);
        }

        _path = path;
        Write(HeaderFormat, Encode(date), Encode(clock), Encode(program), FormatFileTimes(fileSystem?.GetWriteTime(program)));
    }

    public bool IsOpen => _stream is not null;

    private static ReadOnlySpan<byte> HeaderFormat => "----< %s %s >----< %s (%s) >----"u8;

    private static ReadOnlySpan<byte> ErrorPrefixFormat => "!!!ERROR %s!!!"u8;

    private static ReadOnlySpan<byte> TagSeparator => ": "u8;

    public static string FormatError(LogError code, int value, ReadOnlySpan<byte> text)
    {
        Span<byte> buffer = stackalloc byte[BufferSize];
        int length = CRuntime.PrintFormat(buffer, GetFormat(code), [value, (ReadOnlyMemory<byte>)text.ToArray()]);

        return GameTextEncoding.Cyrillic.GetString(buffer[..length]);
    }

    [EngineRoutine(0x00408950)]
    public void Write(ReadOnlySpan<byte> format, params ReadOnlySpan<FormatArgument> arguments)
    {
        if (_stream is null)
            return;

        Span<byte> buffer = stackalloc byte[BufferSize];
        int length = CRuntime.PrintFormat(buffer, format, arguments);
        WriteText(buffer[..length]);
        _stream.Write(CRuntime.LineEnd);
        _stream.Flush();
    }

    [EngineRoutine(0x00408e00)]
    public void Error(ReadOnlySpan<byte> tag, LogError code, int value, ReadOnlySpan<byte> text, params ReadOnlySpan<FormatArgument> tagArguments)
    {
        if (_stream is null)
            return;

        Span<byte> buffer = stackalloc byte[BufferSize];
        int length = PrintFormat.Format(buffer, ErrorPrefixFormat, [Encode(GetCurrentTime())]);
        length += PrintFormat.Format(buffer[length..], tag, tagArguments);
        length += Append(buffer[length..], TagSeparator);
        length += Append(buffer[length..], GetFormat(code));
        Write(buffer[..length], value, (ReadOnlyMemory<byte>)text.ToArray());
    }

    [EngineRoutine(0x00408770)]
    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;

        if (_fileSystem is null || _path.Equals(LastPath, StringComparison.OrdinalIgnoreCase) || _path.Equals(FallbackPath, StringComparison.OrdinalIgnoreCase))
            return;

        _fileSystem.Delete(LastPath);

        if (!_fileSystem.Move(_path, LastPath))
        {
            _fileSystem.Delete(FallbackPath);
            _fileSystem.Move(_path, FallbackPath);
        }
    }

    private static ReadOnlySpan<byte> GetFormat(LogError code) => code switch
    {
        LogError.Lock => "0x%X Couldn't lock %s"u8,
        LogError.Copy => "0x%X Couldn't copy %s"u8,
        LogError.Memory => "%i There was not enough memory for %s"u8,
        LogError.Create => "0x%X Couldn't create the %s"u8,
        LogError.Invalid => "0x%X Invalid %s"u8,
        LogError.Load => "0x%X Load %s"u8,
        LogError.Save => "0x%X Save %s"u8,
        LogError.Open => "0x%X Couldn't open '%s'"u8,
        LogError.Set => "0x%X Couldn't set the %s"u8,
        LogError.Get => "0x%X Couldn't get the %s"u8,
        LogError.Text => "%i %s"u8,
        LogError.Section => "0x%X Section can't found (%s)"u8,
        LogError.Initialize => "0x%X Unable initialize %s"u8,
        LogError.Missing => "%i Missing %s"u8,
        LogError.Unknown => "%i Unknownn %s"u8,
        _ => default
    };

    private static int Append(Span<byte> destination, ReadOnlySpan<byte> text)
    {
        int count = Math.Min(text.Length, destination.Length);
        text[..count].CopyTo(destination);

        return count;
    }

    [EngineRoutine(0x00406900)]
    private static ReadOnlyMemory<byte> FormatFileTimes(DateTime? written)
    {
        if (written is null)
            return ReadOnlyMemory<byte>.Empty;

        string stamp = written.Value.ToString($"{DateFormat} hh:mm:ss", CultureInfo.InvariantCulture);

        return Encode($"{CreatedPrefix}{stamp}{AccessedPrefix}{stamp}{WrittenPrefix}{stamp}");
    }

    private static ReadOnlyMemory<byte> Encode(string text) => GameTextEncoding.Cyrillic.GetBytes(text);

    [EngineRoutine(0x00406c90)]
    private static string ReplaceFirst(string text, char from, char to)
    {
        int index = text.IndexOf(from);

        return index < 0 ? text : string.Concat(text.AsSpan(0, index), new ReadOnlySpan<char>(in to), text.AsSpan(index + 1));
    }

    private string GetCurrentTime() => _time.GetLocalNow().ToString(TimeFormat, CultureInfo.InvariantCulture);

    private void WriteText(ReadOnlySpan<byte> text)
    {
        while (!text.IsEmpty)
        {
            int newline = text.IndexOf((byte)'\n');

            if (newline < 0)
            {
                _stream!.Write(text);

                return;
            }

            _stream!.Write(text[..newline]);
            _stream.Write(CRuntime.LineEnd);
            text = text[(newline + 1)..];
        }
    }
}
