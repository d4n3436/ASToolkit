using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents a definition whose blended, depth-buffered sprite file holds a depth and an ARGB4444 color for each pixel and is drawn as one quad faded against the depth buffer.
/// </summary>
public sealed class DepthFadedSpriteDefinition : SoftwareSpriteDefinition
{
    private const int FadeRange = 0x7f;
    private const int FadeShift = 7;
    private const int AlphaBits = 12;
    private const int FadeAlphaShift = 3;
    private const ushort OpaqueTexelAlpha = 0xf000;
    private const ushort ColorMask = 0xfff;
    private const int FadedPixelSize = 4;

    /// <summary>
    /// Initializes a new instance of the <see cref="DepthFadedSpriteDefinition"/> class.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <param name="gameObject">The object record.</param>
    /// <param name="weapon">The weapon record.</param>
    /// <param name="file">The sprite file.</param>
    /// <param name="totalFrames">The frame count of the definition's sprite.</param>
    /// <param name="format">The channel layout of the 16-bit back buffer.</param>
    public DepthFadedSpriteDefinition(KnownObject id, in GameObject gameObject, in WeaponRecord weapon, in VidResource file, int totalFrames, HighColorFormat format)
        : base(id, gameObject, weapon, file, totalFrames, format)
    {
    }

    /// <summary>
    /// Sets the gamma of an army and leaves the colors of the sprite file as they are.
    /// </summary>
    /// <param name="gamma">The gamma.</param>
    /// <param name="army">The army, or <see cref="SpriteDefinition.AllArmies"/> for every army.</param>
    /// <param name="ambient">The ambient gamma of the level.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="army"/> is above <see cref="SpriteDefinition.AllArmies"/>.</exception>
    [EngineRoutine(0x004127e0)]
    public override void SetGamma(Gamma gamma, int army, Gamma ambient) => StoreGamma(gamma, army);

    /// <inheritdoc/>
    [EngineRoutine(0x0041b880)]
    public override void SetLayer()
    {
        var properties = Object.Properties;
        if (properties.HasFlag(ObjectProperties.Ground))
        {
            Layer = ObjectLayer.Ground2;
        }
        else if (Object.SpriteType == SpriteType.Region)
        {
            Layer = ObjectLayer.Region;
        }
        else if (!FileType.HasFlag(VidType.Blended))
        {
            Layer = ObjectLayer.Hardware;
        }
        else
        {
            Layer = !FileType.HasFlag(VidType.ZBuffer) && properties.HasFlag(ObjectProperties.Wave) ? ObjectLayer.AlphaGround : ObjectLayer.Alpha;
        }
    }

    /// <summary>
    /// Writes a run of faded colors that show through where the sprite is no deeper than what is drawn underneath and keep the alpha of the sprite.
    /// </summary>
    /// <param name="offsets">The depths of the run's pixels relative to the sprite.</param>
    /// <param name="colors">The ARGB4444 colors of the run.</param>
    /// <param name="depths">The depth buffer under the run.</param>
    /// <param name="output">The texels to write.</param>
    /// <param name="count">The number of pixels.</param>
    /// <param name="key">The depth of the sprite.</param>
    [EngineRoutine(0x0041af80)]
    public static void FadeColors(ReadOnlySpan<short> offsets, ReadOnlySpan<ushort> colors, ReadOnlySpan<ushort> depths, Span<ushort> output, int count, short key)
    {
        for (int i = 0; i < count; i++)
        {
            int depth = (short)depths[i];
            int value = key + offsets[i];
            if (value < depth)
            {
                output[i] = 0;
            }
            else if (depth + FadeRange < value)
            {
                output[i] = colors[i];
            }
            else
            {
                int scaled = (int)((uint)(key - depth + offsets[i]) * colors[i]) >> FadeShift;
                ushort alpha = scaled < ushort.MaxValue + 1 ? (ushort)(scaled & OpaqueTexelAlpha) : OpaqueTexelAlpha;
                output[i] = (ushort)((colors[i] & ColorMask) + alpha);
            }
        }
    }

    /// <summary>
    /// Writes a run of colors that fade in by depth, from clear where the sprite lies behind what is drawn underneath to opaque where it lies well in front.
    /// </summary>
    /// <param name="offsets">The depths of the run's pixels relative to the sprite.</param>
    /// <param name="colors">The colors of the run, without alpha.</param>
    /// <param name="depths">The depth buffer under the run.</param>
    /// <param name="output">The texels to write.</param>
    /// <param name="count">The number of pixels.</param>
    /// <param name="key">The depth of the sprite.</param>
    [EngineRoutine(0x0041b040)]
    public static void FadeOpaque(ReadOnlySpan<short> offsets, ReadOnlySpan<ushort> colors, ReadOnlySpan<ushort> depths, Span<ushort> output, int count, short key)
    {
        for (int i = 0; i < count; i++)
        {
            int depth = (short)depths[i];
            int value = key + offsets[i];
            if (value < depth)
            {
                output[i] = 0;
            }
            else if (depth + FadeRange < value)
            {
                output[i] = (ushort)(colors[i] | OpaqueTexelAlpha);
            }
            else
            {
                int fade = key - depth + offsets[i];
                output[i] = (ushort)((fade + (fade >> 31 & 7) >> FadeAlphaShift << AlphaBits) | colors[i]);
            }
        }
    }

    /// <summary>
    /// Draws a sprite whose runs hold a depth and an ARGB4444 color for each pixel, faded by the depth buffer.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="map">The map.</param>
    /// <param name="sprite">The sprite.</param>
    /// <param name="camera">The top left corner of the view, in map pixels.</param>
    [EngineRoutine(0x0041b0e0)]
    public override void Draw(Screen screen, Map map, Sprite sprite, Vector2 camera)
    {
        if (IsHidden)
            return;

        var clip = screen.Clip;

        var origin = new Point((int)(sprite.Position.X - camera.X - FrameSize.Center.X), (int)(sprite.Position.Y - sprite.Position.Z - camera.Y - FrameSize.Center.Y));
        if (IsOutside(clip, origin, FrameSize))
            return;

        int z = GetDepth(map, sprite, ref origin);
        if (!ReadFrame(Buffer, clip, FrameOffsets[sprite.Frame], origin.Y, out int top, out int rowCount, out int bottom, out int position))
            return;

        SkipRows(Buffer, ref position, ref top, ref rowCount, clip.Top, FadedPixelSize);
        rowCount = bottom - top;
        screen.EnsureScratch(FrameSize with { Height = rowCount });
        int scratchWidth = screen.ScratchSize.Width;
        var scratch = screen.Scratch;
        short key = (short)z;
        bool additive = !FileType.HasFlag(VidType.Blended) || !FileType.HasFlag(VidType.Runs8Bit);
        bool clipped = origin.X < clip.Left || origin.X + FrameSize.Width > clip.Right;
        for (int row = 0; row < rowCount; row++)
        {
            var output = scratch.Slice(row * scratchWidth, scratchWidth);
            output.Clear();
            int depthRow = (top + row) * screen.DepthWidth;
            int x = origin.X;
            while (!RunAt(Buffer, position).IsEnd)
            {
                var run = RunAt(Buffer, position);
                x += run.Skip;
                int count = run.Count;
                position += RunHeader;
                if (ClipRunToRow(clip, clipped, x, count, out int first, out int skip, out int length))
                {
                    length = Math.Min(length, Math.Min(scratchWidth - (first - origin.X), screen.Depth.Length - depthRow - first));
                    var offsets = MemoryMarshal.Cast<byte, short>(Buffer.Slice(position + skip * sizeof(short), length * sizeof(short)));
                    var colors = MemoryMarshal.Cast<byte, ushort>(Buffer.Slice(position + (count + skip) * sizeof(short), length * sizeof(ushort)));
                    var under = screen.Depth.Slice(depthRow + first, length);
                    var target = output.Slice(first - origin.X, length);
                    if (additive)
                    {
                        FadeOpaque(offsets, colors, under, target, length, key);
                    }
                    else
                    {
                        FadeColors(offsets, colors, under, target, length, key);
                    }
                }

                position += count * FadedPixelSize;
                x += count;
            }

            position += RunHeader;
        }

        var tint = GetTint(map, sprite);
        var destination = Rectangle.FromLTRB(origin.X, top, origin.X + FrameSize.Width, bottom);
        var source = new Rectangle(0, 0, FrameSize.Width, rowCount);
        new Texture(scratch, screen.ScratchSize, VidPixelFormat.A4R4G4B4).DrawFlatQuad(screen, destination, source, tint, additive ? QuadBlend.AlphaAdd : QuadBlend.Alpha);
    }
}
