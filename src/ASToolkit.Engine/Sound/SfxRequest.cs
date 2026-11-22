namespace ASToolkit.Engine;

internal struct SfxRequest
{
    public SfxRequest(int id, int pan, int volume)
    {
        Id = id;
        Pan = pan;
        Volume = volume;
    }

    public int Id { get; set; }

    public int Pan { get; }

    public int Volume { get; set; }
}
