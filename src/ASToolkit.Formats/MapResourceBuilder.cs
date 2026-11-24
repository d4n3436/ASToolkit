using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace ASToolkit.Formats;

/// <summary>
/// Represents the placed objects of a .map file.
/// </summary>
public sealed class MapResourceBuilder
{
    private const int EntrySize = 28;
    private const int PlayerCount = 4;
    private const int EndHandle = -1;
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
    /// Gets the graphics record of the map.
    /// </summary>
    public MapGraphics Graphics { get; init; }

    /// <summary>
    /// Gets the groups of sprites of the map, each a list of sprite handles.
    /// </summary>
    public IReadOnlyList<int[]> Groups { get; init; } = [];

    /// <summary>
    /// Gets the sprite handle of the player each player slot starts with, or 0 if the slot doesn't start with a player.
    /// </summary>
    public IReadOnlyList<int> PlayerHandles { get; init; } = [];

    /// <summary>
    /// Gets the gamma of the ambient light written over the template's, or null to keep the template's light.
    /// </summary>
    public Gamma? Ambient { get; init; }

    /// <summary>
    /// Gets the version of the map file's format, or 0 for a new map.
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// Gets the game time the map file was saved at.
    /// </summary>
    public int Tick { get; init; }

    /// <summary>
    /// Gets the painted height grid, one height per 8 by 8 pixel cell, or an empty grid when the map doesn't have one.
    /// </summary>
    public ReadOnlyMemory<short> HeightGrid { get; init; }

    /// <summary>
    /// Reads the placed objects of a map. Entries with non-finite or far-off coordinates are skipped.
    /// </summary>
    /// <param name="path">The path of the map file.</param>
    /// <returns>The placed objects of the map.</returns>
    public static MapResourceBuilder Load(string path) => Parse(File.ReadAllBytes(path));

    /// <summary>
    /// Reads the placed objects of a map from its bytes. Entries with non-finite or far-off coordinates are skipped.
    /// </summary>
    /// <param name="data">The bytes of the map.</param>
    /// <returns>The placed objects of the map.</returns>
    public static MapResourceBuilder Parse(ReadOnlyMemory<byte> data)
    {
        var resource = new MapResource(data);
        var payloads = new Dictionary<int, ReadOnlyMemory<byte>>();
        foreach (var spriteData in resource.SpriteData)
        {
            payloads[spriteData.Handle] = spriteData.Payload;
        }

        var map = new MapResourceBuilder
        {
            Size = new SizeF(resource.Header.MapW, resource.Header.MapH),
            Scroll = resource.Header.Camera,
            Graphics = resource.Graphics,
            Groups = resource.Groups.ToArray(),
            PlayerHandles = resource.PlayerHandles.ToArray(),
            Version = resource.Header.Version,
            Tick = resource.Header.Tick,
            HeightGrid = resource.HeightGrid.ToArray()
        };
        foreach (var sprite in resource.Sprites)
        {
            var position = sprite.Position;
            if (!IsSane(position.X) || !IsSane(position.Y) || !IsSane(position.Z))
            {
                continue;
            }

            map.Sprites.Add(new MergedMapSprite(sprite.Handle, sprite.Id, position, sprite.Direction, sprite.Army,
                payloads.TryGetValue(sprite.Handle, out var payload) ? payload.ToArray() : new byte[8], sprite.Gamma) { Scale = sprite.Scale });
        }

        return map;
    }

    /// <summary>
    /// Builds the bytes of a map file.
    /// </summary>
    /// <returns>The bytes of the map.</returns>
    /// <remarks>
    /// The chunks follow in the order GRPH, HEAD, GRID, SPR, SPRD, PLAY and GROU. The header and the objects keep integer coordinates for a version below 10.
    /// </remarks>
    public byte[] ToBytes()
    {
        bool isInteger = Version < MapHeader.FloatVersion;
        var file = new ResContainerBuilder { Magic = "RES "u8.ToArray(), Kind = "MAP "u8.ToArray() };
        var graphics = Ambient is null ? Graphics : new MapGraphics(Graphics.Environment, Ambient.Value, Graphics.Direction, Graphics.DirectionLength);
        AddChunk(file, "GRPH", [MemoryMarshal.AsBytes(new ReadOnlySpan<MapGraphics>(in graphics)).ToArray()]);
        var head = isInteger
            ? new MapHeader(new Size((int)Size.Width, (int)Size.Height), new Point((int)Scroll.X, (int)Scroll.Y), Tick, Version)
            : new MapHeader(Size, Scroll, Tick, Version);
        AddChunk(file, "HEAD", [MemoryMarshal.AsBytes(new ReadOnlySpan<MapHeader>(in head)).ToArray()]);
        if (!HeightGrid.IsEmpty)
        {
            AddChunk(file, "GRID", [MemoryMarshal.AsBytes(HeightGrid.Span).ToArray()]);
        }

        byte[] entries = new byte[Sprites.Count * EntrySize + sizeof(int)];
        var payloads = new List<byte[]>(Sprites.Count + 1);
        for (int i = 0; i < Sprites.Count; i++)
        {
            var sprite = Sprites[i];
            var position = isInteger ? Vector128.ConvertToInt32(sprite.Position.AsVector128()).AsSingle().AsVector3() : sprite.Position;
            MemoryMarshal.Write(entries.AsSpan(i * EntrySize), new OldMapSprite(sprite.Handle, sprite.Id, position, sprite.Direction, sprite.Army));
            if (sprite.Payload.Length == 0)
                continue;

            byte[] payload = new byte[sizeof(int) + sprite.Payload.Length];
            MemoryMarshal.Write(payload, sprite.Handle);
            sprite.Payload.CopyTo(payload, sizeof(int));
            payloads.Add(payload);
        }

        EndMarker.CopyTo(entries, entries.Length - sizeof(int));
        payloads.Add(EndMarker);
        AddChunk(file, "SPR ", [entries]);
        AddChunk(file, "SPRD", payloads);
        byte[] players = new byte[PlayerCount * sizeof(int)];
        for (int i = 0; i < PlayerCount && i < PlayerHandles.Count; i++)
        {
            MemoryMarshal.Write(players.AsSpan(i * sizeof(int)), PlayerHandles[i]);
        }

        AddChunk(file, "PLAY", [players]);
        var groups = new List<int>();
        foreach (int[] group in Groups)
        {
            groups.AddRange(group);
            groups.Add(EndHandle);
        }

        groups.Add(EndHandle);
        AddChunk(file, "GROU", [MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(groups)).ToArray()]);

        return file.Build();
    }

    /// <summary>
    /// Writes the map to a file and takes every chunk other than the objects and the header from a template map.
    /// </summary>
    /// <param name="path">The path to write the map to.</param>
    /// <param name="templatePath">The path of the template map file.</param>
    public void Write(string path, string templatePath) => File.WriteAllBytes(path, ToBytes(File.ReadAllBytes(templatePath)));

    /// <summary>
    /// Builds the bytes of the map and takes every chunk other than the objects and the header from the bytes of a template map.
    /// </summary>
    /// <param name="template">The bytes of the template map.</param>
    /// <returns>The bytes of the map.</returns>
    public byte[] ToBytes(ReadOnlyMemory<byte> template)
    {
        var file = ResContainerBuilder.Parse(template);
        byte[] entries = new byte[Sprites.Count * EntrySize + 4];
        var payloads = new List<byte[]>(Sprites.Count + 1);
        for (int spriteIndex = 0; spriteIndex < Sprites.Count; spriteIndex++)
        {
            var sprite = Sprites[spriteIndex];
            MemoryMarshal.Write(entries.AsSpan(spriteIndex * EntrySize), new OldMapSprite(sprite.Handle, sprite.Id, sprite.Position, sprite.Direction, sprite.Army)); // TODO: Handle AS2 map sprites and avoid hardcoding the size
            byte[] payload = new byte[4 + sprite.Payload.Length];
            MemoryMarshal.Write(payload, sprite.Handle);
            sprite.Payload.CopyTo(payload, 4);
            payloads.Add(payload);
        }

        EndMarker.CopyTo(entries, entries.Length - 4);
        payloads.Add(EndMarker);
        file.GetChunk("SPR ").Records = [entries];
        file.GetChunk("SPRD").Records = payloads;
        var head = new MapHeader(Size, Scroll, 1999, 12);
        file.GetChunk("HEAD").Records = [MemoryMarshal.AsBytes(new ReadOnlySpan<MapHeader>(in head)).ToArray()];
        if (Ambient is not null)
        {
            byte[] record = file.GetChunk("GRPH").Records[0].ToArray();
            ref readonly var graphics = ref MemoryMarshal.AsRef<MapGraphics>(record);
            MemoryMarshal.Write(record, new MapGraphics(graphics.Environment, Ambient.Value, graphics.Direction, graphics.DirectionLength));
            file.GetChunk("GRPH").Records = [record];
        }

        // Drop the template's groups since they reference its sprite handles
        var groups = file.FindChunk("GROU");
        groups?.Records = [EndMarker];

        return file.Build();
    }

    private static void AddChunk(ResContainerBuilder file, string tag, List<byte[]> records) =>
        file.Chunks.Add(new ResChunkBuilder { Tag = tag, Flags = ResChunkFlags.ExtendedHeader, Records = records });

    private static bool IsSane(float value) => float.IsFinite(value) && Math.Abs(value) < 1e6f;
}
