namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents a read-only view of a sound of the SFX chunk of Alien Shooter's objects.res.
/// </summary>
public readonly struct SfxRecord
{
    /// <summary>
    /// Represents the maximum number of samples a sound holds.
    /// </summary>
    public const int MaxSamples = 8;

    private readonly ReadOnlyMemory<byte> _data;

    /// <summary>
    /// Creates a sound over the bytes of its record.
    /// </summary>
    /// <param name="data">The bytes of the record.</param>
    public SfxRecord(ReadOnlyMemory<byte> data)
    {
        _data = data;
    }

    /// <summary>
    /// Gets the number of voices the sound may play at once, in tenths.
    /// </summary>
    public byte VoiceLimit => _data.Span[0];

    /// <summary>
    /// Gets the number of samples before the first empty path.
    /// </summary>
    public int SampleCount
    {
        get
        {
            int count = 0;
            while (count < MaxSamples && !GetSamplePath(count).IsEmpty)
            {
                count++;
            }

            return count;
        }
    }

    /// <summary>
    /// Returns the path of a sample, empty past the last one.
    /// </summary>
    /// <param name="index">The index of the sample.</param>
    /// <returns>The path, in cp1251 encoding.</returns>
    public ReadOnlySpan<byte> GetSamplePath(int index)
    {
        var rest = _data.Span[1..];
        for (int sample = 0; sample <= index && !rest.IsEmpty; sample++)
        {
            int end = rest.IndexOfAny((byte)0, (byte)'\n');
            if (end < 0)
            {
                end = rest.Length;
            }

            if (sample == index)
                return rest[..end].TrimEnd((byte)'\r');

            rest = rest[Math.Min(end + 1, rest.Length)..];
        }

        return [];
    }
}
