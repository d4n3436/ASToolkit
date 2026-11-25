using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using ASToolkit.Core;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the shared data and frames of a built terrain object ID whose sprites are drawn in software into a 16-bit back buffer and into the textures of the hardware ground.
/// </summary>
public sealed class VidHighColor : VidSoftware
{
    /// <summary>
    /// Initializes a new instance of the <see cref="VidHighColor"/> class.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <param name="gameObject">The object record.</param>
    /// <param name="weapons">The weapon table.</param>
    /// <param name="file">The sprite file.</param>
    /// <param name="totalFrames">The frame count of the sprite file.</param>
    /// <param name="format">The channel layout of the 16-bit back buffer.</param>
    public VidHighColor(KnownObject id, in GameObject gameObject, AlienShooterWeaponRecord[] weapons, in VidResource file, int totalFrames, HighColorFormat format)
        : base(id, gameObject, weapons, file, totalFrames, format)
    {
    }

    private VidHighColor(VidHighColor source)
        : base(source)
    {
    }

    [EngineRoutine(0x004126e0)]
    internal override Vid CreateMirror() => new VidHighColor(this);

    /// <inheritdoc/>
    protected override bool IsHighColor => true;

    /// <summary>
    /// Gets the size of one palette in bytes, with 16-bit colors unless the sprite file is blended.
    /// </summary>
    /// <returns>The size of one palette.</returns>
    [EngineRoutine(0x00412710)]
    public override int GetPaletteSize() => FileType.HasFlag(VidType.Blended) ? TrueColorPaletteSize : HighColorPaletteSize;

    /// <summary>
    /// Draws a sprite with 16-bit pixels from its runs.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="map">The map.</param>
    /// <param name="sprite">The sprite.</param>
    /// <param name="camera">The top left corner of the view, in map pixels.</param>
    [EngineRoutine(0x00417780)]
    public override void Draw(Screen screen, Map map, Sprite sprite, Vector2 camera)
    {
        if (IsHidden)
            return;

        var origin = GetScreenOrigin(sprite, camera);
        if (!TryLocateFrame(map, sprite, screen.Viewport, ref origin, out int depth, out var rows))
            return;

        Span<byte> scratch = stackalloc byte[PaletteScratchSize];
        var target = new RunTarget<ushort>(screen.HighColorPixels, screen.Pitch, screen.Depth, screen.DepthWidth, screen.Viewport, screen.Format);
        DrawRuns(target, SelectPalette(map, sprite, scratch), origin, depth, rows);
    }

    /// <summary>
    /// Draws a sprite with 16-bit pixels into the color and depth of a tile of the hardware ground.
    /// </summary>
    /// <param name="map">The map.</param>
    /// <param name="sprite">The sprite.</param>
    /// <param name="tile">The tile.</param>
    /// <param name="colorTexture">The color texture of <paramref name="tile"/>.</param>
    /// <param name="depthTexture">The depth texture of <paramref name="tile"/>.</param>
    /// <param name="tileViewport">The area of the textures that is drawn to, in texels.</param>
    [EngineRoutine(0x00418940)]
    public override void DrawToVid(Map map, Sprite sprite, HardwareTile tile, TextureData colorTexture, TextureData depthTexture, Rectangle tileViewport)
    {
        if (IsHidden)
            return;

        float worldX = sprite.Position.X - FrameSize.Center.X;
        worldX -= tile.Origin.X;
        worldX -= tile.Offset.X;
        float worldY = sprite.Position.Y - sprite.Position.Z - FrameSize.Center.Y;
        worldY -= tile.Origin.Y;
        worldY -= tile.Offset.Y;
        var origin = new Point((int)worldX, (int)worldY);
        if (!TryLocateFrame(map, sprite, tileViewport, ref origin, out int depth, out var rows))
            return;

        var target = new RunTarget<ushort>(colorTexture.Texels, colorTexture.Size.Width, depthTexture.Texels, depthTexture.Size.Width, tileViewport, Format);
        DrawRuns(target, Palettes[GetPaletteOffset(sprite)..], origin, depth, rows);
    }

    /// <summary>
    /// Applies a gamma to a palette of 16-bit colors, or of 32-bit colors when the sprite file is blended.
    /// </summary>
    /// <param name="palette">The palette, 256 entries of the size the sprite file stores them in.</param>
    /// <param name="gamma">The gamma.</param>
    [EngineRoutine(0x00416030)]
    public override void ApplyGamma(Span<byte> palette, Gamma gamma)
    {
        if (palette.IsEmpty || gamma is { Negative: 0, Positive: 0 })
            return;

        if (FileType.HasFlag(VidType.Blended))
        {
            base.ApplyGamma(palette, gamma);
            return;
        }

        var pixels = MemoryMarshal.Cast<byte, ushort>(palette);
        for (int i = 0; i < PaletteEntries; i++)
        {
            uint color = Format.Expand(pixels[i]);
            uint blue = (uint)((pixels[i] & HighColorBlueMask) << 3);
            uint green = color >> Gamma.ChannelBits & byte.MaxValue;
            uint red = color >> (2 * Gamma.ChannelBits) & byte.MaxValue;
            pixels[i] = Format.Pack(gamma.Apply(red << (2 * Gamma.ChannelBits) | green << Gamma.ChannelBits | blue));
        }
    }
}
