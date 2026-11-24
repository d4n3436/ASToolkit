using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

/// <summary>
/// Represents a wav sample, a RIFF container of kind WAVE.
/// </summary>
public readonly struct WaveResource
{
    private const int MinFormatSize = 14;
    private readonly ReadOnlyMemory<byte> _data;

    /// <summary>
    /// Initializes a new instance of the <see cref="WaveResource"/> structure.
    /// </summary>
    /// <param name="data">The bytes of the file.</param>
    public WaveResource(ReadOnlyMemory<byte> data)
    {
        _data = data;
    }

    /// <summary>
    /// Gets the format of the sample, or the default when the file doesn't have an fmt chunk of 14 bytes or more.
    /// </summary>
    public WaveFormat Format
    {
        get
        {
            int length = Unsafe.SizeOf<WaveFormat>();
            if (!new ResContainer(_data).TryGetChunk("fmt "u8, out var chunk) || chunk.Size < MinFormatSize)
                return default;

            var format = chunk.ReadBody(length);
            return format.Length < length ? default : MemoryMarshal.Read<WaveFormat>(format.Span);
        }
    }

    /// <summary>
    /// Gets the sample data, or empty if the file doesn't have a data chunk.
    /// </summary>
    public ReadOnlyMemory<byte> Samples => new ResContainer(_data).TryGetChunk("data"u8, out var chunk) ? chunk.Body : default;
}
