using System.Globalization;
using System.Text;
using ASToolkit.Formats;

namespace ASToolkit.Engine.IO;

internal sealed class MyError : IDisposable
{
    private const string NamePrefix = "logs\\error";
    private const string FolderPrefix = "logs\\";
    private const string NameExtension = ".log";
    private const string LastPath = "logs\\error.log";
    private const string FallbackPath = "error.log";
    private const string CreatedPrefix = "Cr-";
    private const string AccessedPrefix = " La-";
    private const string WrittenPrefix = " Lw-";

    private readonly IGameFileSystem? _fileSystem;
    private readonly TimeProvider _time;
    private readonly string _path;
    private Stream? _stream;

    [EngineRoutine(0x004084a0)]
    public MyError(IGameFileSystem? fileSystem, TimeProvider time, string program, bool isTimestamped = true)
    {
        _fileSystem = fileSystem;
        _time = time;
        string path = $"{NamePrefix}{MyLib.GetDateString(time)} {MyLib.GetTimeString(time)}{NameExtension}".ReplaceFirst(':', 'h').ReplaceFirst(':', 'm');
        if (!isTimestamped)
        {
            path = LastPath;
        }

        _stream = fileSystem?.OpenWrite(path, !isTimestamped);

        if (_stream is null)
        {
            path = path.After(FolderPrefix);
            _stream = fileSystem?.OpenWrite(path, !isTimestamped);
        }

        _path = path;
        Log($"----< {MyLib.GetDateString(time)} {MyLib.GetTimeString(time)} >----< {program} ({FormatFileTimes(fileSystem?.GetWriteTime(program))}) >----");
    }

    public bool IsOpen => _stream is not null;

    public static string FormatError(LogError code, int value, ReadOnlySpan<byte> text)
    {
        string name = Decode(text);

        return code switch
        {
            LogError.Lock => $"0x{value:X} Couldn't lock {name}",
            LogError.Copy => $"0x{value:X} Couldn't copy {name}",
            LogError.Memory => $"{value} There was not enough memory for {name}",
            LogError.Create => $"0x{value:X} Couldn't create the {name}",
            LogError.Invalid => $"0x{value:X} Invalid {name}",
            LogError.Load => $"0x{value:X} Load {name}",
            LogError.Save => $"0x{value:X} Save {name}",
            LogError.Open => $"0x{value:X} Couldn't open '{name}'",
            LogError.Set => $"0x{value:X} Couldn't set the {name}",
            LogError.Get => $"0x{value:X} Couldn't get the {name}",
            LogError.Text => $"{value} {name}",
            LogError.Section => $"0x{value:X} Section can't found ({name})",
            LogError.Initialize => $"0x{value:X} Unable initialize {name}",
            LogError.Missing => $"{value} Missing {name}",
            LogError.Unknown => $"{value} Unknownn {name}",
            _ => string.Empty
        };
    }

    public static string Decode(ReadOnlySpan<byte> text) => GameTextEncoding.Cyrillic.GetString(CRuntime.GetString(text));

    public void Log(ReadOnlySpan<byte> text)
    {
        if (_stream is null)
            return;

        WriteLine(text);
    }

    [EngineRoutine(0x00408770)]
    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;

        if (_fileSystem is null || _path == LastPath || _path == FallbackPath)
            return;

        _fileSystem.Delete(LastPath);

        if (!_fileSystem.Move(_path, LastPath))
        {
            _fileSystem.Delete(FallbackPath);
            _fileSystem.Move(_path, FallbackPath);
        }
    }

    [EngineRoutine(0x00408840)]
    public void Window(string text) => Log(text);

    [EngineRoutine(0x00408950)]
    public void Log(string text)
    {
        if (_stream is null)
            return;

        WriteLine(GameTextEncoding.Cyrillic.GetBytes(text));
    }

    [EngineRoutine(0x004089b0)]
    public void Fatal(string text)
    {
        Log(text);
        _stream?.Dispose();
        _stream = null;
    }

    [EngineRoutine(0x00408e00)]
    public void Error(string tag, LogError code, int value, ReadOnlySpan<byte> text = default)
    {
        if (_stream is null)
            return;

        string time = _time.GetLocalNow().ToString(MyLib.TimeFormat, CultureInfo.InvariantCulture);
        Log($"!!!ERROR {time}!!!{tag}: {FormatError(code, value, text)}");
    }

    public void Error(ReadOnlySpan<byte> tag, LogError code, int value, ReadOnlySpan<byte> text = default) => Error(Decode(tag), code, value, text);

    [EngineRoutine(0x00406900)]
    private static string FormatFileTimes(DateTime? written)
    {
        string stamp = (written ?? new DateTime(1601, 1, 1)).ToString($"{MyLib.DateFormat} hh:mm:ss", CultureInfo.InvariantCulture);

        return $"{CreatedPrefix}{stamp}{AccessedPrefix}{stamp}{WrittenPrefix}{stamp}";
    }

    private void WriteLine(ReadOnlySpan<byte> text)
    {
        WriteText(text);
        _stream!.Write(CRuntime.LineEnd);
        _stream.Flush();
    }

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
