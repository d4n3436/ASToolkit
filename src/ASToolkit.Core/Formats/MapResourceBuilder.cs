using System.Numerics;
using System.Runtime.InteropServices;
using System.Drawing;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents the placed objects of a .map file.
/// </summary>
public sealed class MapResourceBuilder
{
    private const int EntrySize = 28;
    private static readonly byte[] EndMarker = [0xff, 0xff, 0xff, 0xff];

    /// <summary>
    /// Gets the placed objects in file order.
    /// </summary>
    public List<MergedMapSprite> Sprites { get; init; } = [];

    /// <summary>
    /// Gets or sets the map width and height in pixels.
    /// </summary>
    public SizeF Size { get; set; }

    /// <summary>
    /// Gets or sets the camera position at the start.
    /// </summary>
    public Vector2 Scroll { get; set; }

    /// <summary>
    /// Gets the graphics record the map was read with.
    /// </summary>
    public MapGraphics Graphics { get; init; }

    /// <summary>
    /// Reads the placed objects of a map. Entries with non-finite or far-off coordinates are skipped.
    /// </summary>
    public static MapResourceBuilder Load(string path) => Parse(File.ReadAllBytes(path));

    /// <summary>
    /// Reads the placed objects of a map from its bytes. Entries with non-finite or far-off coordinates are skipped.
    /// </summary>
    public static MapResourceBuilder Parse(ReadOnlyMemory<byte> data)
    {
        var resource = new MapResource(data);
        var payloads = new Dictionary<uint, ReadOnlyMemory<byte>>();
        foreach (var spriteData in resource.SpriteData)
        {
            payloads[spriteData.Handle] = spriteData.Payload;
        }

        var map = new MapResourceBuilder
        {
            Size = new SizeF(resource.Header.MapW, resource.Header.MapH),
            Scroll = resource.Header.Camera,
            Graphics = resource.Graphics
        };
        foreach (var sprite in resource.Sprites)
        {
            var position = sprite.Position;
            if (!IsSane(position.X) || !IsSane(position.Y) || !IsSane(position.Z))
            {
                continue;
            }

            map.Sprites.Add(new MergedMapSprite(sprite.Handle, sprite.Id, position, sprite.Dir, sprite.Army,
                payloads.TryGetValue(sprite.Handle, out var payload) ? payload.ToArray() : new byte[8], sprite.NegativeTint, sprite.PositiveTint) { Scale = sprite.Scale });
        }

        return map;
    }

    /// <summary>
    /// Writes the map, taking every chunk other than the objects and the header from a template map.
    /// </summary>
    public void Write(string path, string templatePath) => File.WriteAllBytes(path, ToBytes(File.ReadAllBytes(templatePath)));

    /// <summary>
    /// Returns the bytes of the map, taking every chunk other than the objects and the header from a template map's bytes.
    /// </summary>
    public byte[] ToBytes(ReadOnlyMemory<byte> template)
    {
        var file = ResContainerBuilder.Parse(template);
        byte[] entries = new byte[Sprites.Count * EntrySize + 4];
        var payloads = new List<byte[]>(Sprites.Count + 1);
        for (int spriteIndex = 0; spriteIndex < Sprites.Count; spriteIndex++)
        {
            var s = Sprites[spriteIndex];
            var e = entries.AsSpan(spriteIndex * EntrySize, EntrySize); // TODO: Handle AS2 map sprites and avoid hardcoding the size
            MemoryMarshal.Write(e, s.Handle);
            MemoryMarshal.Write(e[4..], (uint)s.Id);
            MemoryMarshal.Write(e[8..], s.Position);
            MemoryMarshal.Write(e[20..], s.Dir);
            MemoryMarshal.Write(e[24..], s.Army);
            byte[] payload = new byte[4 + s.Payload.Length];
            MemoryMarshal.Write(payload, s.Handle);
            s.Payload.CopyTo(payload, 4);
            payloads.Add(payload);
        }

        EndMarker.CopyTo(entries, entries.Length - 4);
        payloads.Add(EndMarker);
        file.GetChunk("SPR ").Records = [entries];
        file.GetChunk("SPRD").Records = payloads;
        byte[] headArr = new byte[24];
        var head = headArr.AsSpan();
        MemoryMarshal.Write(head, (float)Size.Width);
        MemoryMarshal.Write(head[4..], (float)Size.Height);
        MemoryMarshal.Write(head[8..], Scroll);
        MemoryMarshal.Write(head[16..], 1999);
        MemoryMarshal.Write(head[20..], 12);
        file.GetChunk("HEAD").Records = [headArr];

        // Drop the template's groups since they reference its sprite handles
        var groups = file.FindChunk("GROU");
        groups?.Records = [EndMarker];

        return file.Build();
    }

    private static bool IsSane(float v) => float.IsFinite(v) && Math.Abs(v) < 1e6f;
}
