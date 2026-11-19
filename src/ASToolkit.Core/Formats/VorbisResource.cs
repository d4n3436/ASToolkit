using System.Buffers.Binary;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents an Ogg Vorbis track, read only as far as its channels, rate and length.
/// </summary>
public readonly struct VorbisResource
{
    private const int PageHeaderSize = 27;
    private const int GranuleOffset = 6;
    private const int SegmentCountOffset = 26;
    private const int IdentificationChannelsOffset = 11;
    private const int IdentificationRateOffset = 12;
    private const int IdentificationSize = 16;

    private readonly ReadOnlyMemory<byte> _data;

    /// <summary>
    /// Creates a track over the bytes of its file.
    /// </summary>
    /// <param name="data">The bytes of the file.</param>
    public VorbisResource(ReadOnlyMemory<byte> data)
    {
        _data = data;
    }

    private static ReadOnlySpan<byte> CapturePattern => "OggS"u8;

    private static ReadOnlySpan<byte> IdentificationHeader => "\u0001vorbis"u8;

    /// <summary>
    /// Gets the number of channels, 0 when the file starts with no identification header.
    /// </summary>
    public int Channels
    {
        get
        {
            var header = GetIdentification();

            return header.IsEmpty ? 0 : header[IdentificationChannelsOffset];
        }
    }

    /// <summary>
    /// Gets the samples per second of each channel, 0 when the file starts with no identification header.
    /// </summary>
    public int SampleRate
    {
        get
        {
            var header = GetIdentification();

            return header.IsEmpty ? 0 : BinaryPrimitives.ReadInt32LittleEndian(header[IdentificationRateOffset..]);
        }
    }

    /// <summary>
    /// Gets the samples of each channel, the granule position of the last page; 0 when the file has no page.
    /// </summary>
    public long SampleCount
    {
        get
        {
            var span = _data.Span;
            int last = span.LastIndexOf(CapturePattern);

            return last < 0 || span.Length - last < PageHeaderSize ? 0 : BinaryPrimitives.ReadInt64LittleEndian(span[(last + GranuleOffset)..]);
        }
    }

    private ReadOnlySpan<byte> GetIdentification()
    {
        var span = _data.Span;

        if (span.Length < PageHeaderSize || !span.StartsWith(CapturePattern))
            return [];

        int body = PageHeaderSize + span[SegmentCountOffset];
        if (span.Length < body + IdentificationSize)
            return [];

        var header = span.Slice(body, IdentificationSize);

        return header.StartsWith(IdentificationHeader) ? header : [];
    }
}
