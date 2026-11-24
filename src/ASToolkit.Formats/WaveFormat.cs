using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

/// <summary>
/// Represents the fmt chunk of a wav sample, laid out as <c>WAVEFORMATEX</c>.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 18)]
public readonly struct WaveFormat
{
    /// <summary>
    /// The sample encoding.
    /// </summary>
    [FieldOffset(0)]
    public readonly ushort FormatTag;

    /// <summary>
    /// The number of channels.
    /// </summary>
    [FieldOffset(2)]
    public readonly ushort Channels;

    /// <summary>
    /// The samples per second of each channel.
    /// </summary>
    [FieldOffset(4)]
    public readonly uint SamplesPerSec;

    /// <summary>
    /// The bytes played per second.
    /// </summary>
    [FieldOffset(8)]
    public readonly uint AvgBytesPerSec;

    /// <summary>
    /// The bytes of one sample over every channel.
    /// </summary>
    [FieldOffset(12)]
    public readonly ushort BlockAlign;

    /// <summary>
    /// The bits of one sample of one channel.
    /// </summary>
    [FieldOffset(14)]
    public readonly ushort BitsPerSample;

    /// <summary>
    /// The bytes of extra format data.
    /// </summary>
    [FieldOffset(16)]
    public readonly ushort ExtraSize;

    /// <summary>
    /// Initializes a new instance of the <see cref="WaveFormat"/> structure.
    /// </summary>
    /// <param name="formatTag">The sample encoding.</param>
    /// <param name="channels">The number of channels.</param>
    /// <param name="samplesPerSec">The samples per second of each channel.</param>
    /// <param name="avgBytesPerSec">The average bytes per second.</param>
    /// <param name="blockAlign">The bytes of one sample of every channel.</param>
    /// <param name="bitsPerSample">The bits of one sample of one channel.</param>
    /// <param name="extraSize">The bytes of extra format data.</param>
    public WaveFormat(ushort formatTag, ushort channels, uint samplesPerSec, uint avgBytesPerSec, ushort blockAlign, ushort bitsPerSample, ushort extraSize)
    {
        FormatTag = formatTag;
        Channels = channels;
        SamplesPerSec = samplesPerSec;
        AvgBytesPerSec = avgBytesPerSec;
        BlockAlign = blockAlign;
        BitsPerSample = bitsPerSample;
        ExtraSize = extraSize;
    }
}
