namespace ASToolkit.Core.Engine;

internal struct SfxBuffer
{
    public SfxBuffer(int id, int buffer)
    {
        Id = id;
        Buffer = buffer;
    }

    public int Id { get; set; }

    public int Buffer { get; set; }

    public bool IsPlaying { get; set; }

    public int Volume { get; set; }

    public int Pan { get; set; }

    public int Start { get; set; }
}
