using ASToolkit.Engine.IO;

namespace ASToolkit.Engine;

/// <summary>
/// Represents one image of a picture, read with the image type that fits the extension of its file.
/// </summary>
public sealed class PictureChannel : IDisposable
{
    private readonly IGameFileSystem _fileSystem;
    private readonly MyError? _error;

    /// <summary>
    /// Initializes a new instance of the <see cref="PictureChannel"/> class with an empty image.
    /// </summary>
    /// <param name="fileSystem">The file system the picture files are in.</param>
    public PictureChannel(IGameFileSystem fileSystem)
        : this(fileSystem, null)
    {
    }

    [EngineRoutine(0x00428a80)]
    internal PictureChannel(IGameFileSystem fileSystem, MyError? error)
    {
        _fileSystem = fileSystem;
        _error = error;
        Image = new PictureImage(fileSystem, error);
    }

    /// <summary>
    /// Gets the image of the channel.
    /// </summary>
    public PictureImage Image { get; private set; }

    /// <summary>
    /// Gets the format of the file the image was loaded from.
    /// </summary>
    public PictureFormat Format { get; private set; }

    /// <summary>
    /// Goes to the next frame of the image.
    /// </summary>
    [EngineRoutine(0x00412660)]
    public void Advance() => Image.Advance();

    /// <summary>
    /// Goes back to the first frame of the image.
    /// </summary>
    [EngineRoutine(0x00412670)]
    public void Rewind() => Image.Rewind();

    /// <summary>
    /// Releases the image.
    /// </summary>
    [EngineRoutine(0x00412680)]
    public void Dispose() => Image.Dispose();

    /// <summary>
    /// Replaces the image with one of the type that fits the extension of a file and loads the file into it.
    /// </summary>
    /// <param name="path">The path of the picture file.</param>
    /// <returns><see langword="true"/> if the file was loaded; otherwise, <see langword="false"/>.</returns>
    [EngineRoutine(0x00428ac0)]
    public bool Load(string path)
    {
        Image.Dispose();
        if (HasExtension(path, TgaImage.Extension))
        {
            Format = PictureFormat.Tga;
            Image = new TgaImage(_fileSystem, _error);
        }
        else if (HasExtension(path, ZImage.Extension))
        {
            Format = PictureFormat.Z;
            Image = new ZImage(_fileSystem, _error);
        }
        else if (HasExtension(path, FlcImage.Extension))
        {
            Format = PictureFormat.Flc;
            Image = new FlcImage(_fileSystem, _error);
        }
        else if (HasExtension(path, BmpImage.Extension))
        {
            Format = PictureFormat.Bmp;
            Image = new BmpImage(_fileSystem, _error);
        }
        else
        {
            if (path.Length != 0)
            {
                _error?.Log($"!!!ERROR!!!PICTURE '{path}': Unknown format file");
            }

            Format = PictureFormat.None;
            Image = new PictureImage(_fileSystem, _error);
        }

        return Format != PictureFormat.None && Image.Load(path);
    }

    private static bool HasExtension(string path, string extension) => path.Contains(extension, StringComparison.Ordinal) || path.Contains(extension.ToUpperInvariant(), StringComparison.Ordinal);
}
