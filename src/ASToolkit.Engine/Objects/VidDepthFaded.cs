using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using ASToolkit.Core;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the shared data and frames of an object ID whose frames have a depth and an ARGB4444 color for each pixel, faded against the depth buffer when drawn.
/// </summary>
public sealed class VidDepthFaded : VidSoftware
{
    private const int FadeRange = 0x7f;
    private const int FadeShift = 7;
    private const int AlphaBits = 12;
    private const int FadeAlphaShift = 3;
    private const ushort OpaqueTexelAlpha = 0xf000;
    private const ushort ColorMask = 0xfff;
    private const int FadedPixelSize = 4;

    /// <summary>
    /// Initializes a new instance of the <see cref="VidDepthFaded"/> class.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <param name="gameObject">The object record.</param>
    /// <param name="weapon">The weapon record.</param>
    /// <param name="file">The sprite file.</param>
    /// <param name="totalFrames">The frame count of the sprite file.</param>
    /// <param name="format">The channel layout of the 16-bit back buffer.</param>
    public VidDepthFaded(KnownObject id, in GameObject gameObject, in WeaponRecord weapon, in VidResource file, int totalFrames, HighColorFormat format)
        : base(id, gameObject, weapon, file, totalFrames, format)
    {
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

    /// <inheritdoc/>
    [EngineRoutine(0x0041b880)]
    public override void SetLayer()
    {
        var properties = Properties;
        if (properties.HasFlag(ObjectProperties.Ground))
        {
            Layer = ObjectLayer.Ground2;
        }
        else if (SpriteType == OldSpriteType.Region)
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
    /// Draws a sprite, faded by the depth buffer.
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

        var viewport = screen.Viewport;
        var origin = new Point((int)(sprite.Position.X - camera.X - FrameSize.Center.X), (int)(sprite.Position.Y - sprite.Position.Z - camera.Y - FrameSize.Center.Y));
        if (!TryLocateFrame(map, sprite, viewport, ref origin, out int z, out var rows))
            return;

        var runs = new RunWalker(Frames, rows, origin.X, FrameSize.Width, viewport, FadedPixelSize);
        int rowCount = runs.End - runs.Top;
        int scratchWidth = map.Session.Graphics.ScratchSize.Width;
        var scratch = map.Session.Graphics.Scratch;
        scratch[..(rowCount * scratchWidth)].Clear();
        short key = (short)z;
        bool additive = !FileType.HasFlag(VidType.Blended) || !FileType.HasFlag(VidType.Runs8Bit);
        while (runs.MoveNext())
        {
            if (!runs.IsVisible)
                continue;

            var offsets = MemoryMarshal.Cast<byte, short>(Frames.Slice(runs.Pixels + runs.Offset * sizeof(short), runs.Count * sizeof(short)));
            var colors = MemoryMarshal.Cast<byte, ushort>(Frames.Slice(runs.Pixels + (runs.Length + runs.Offset) * sizeof(short), runs.Count * sizeof(ushort)));
            var under = screen.Depth.Slice(runs.Row * screen.DepthWidth + runs.First, runs.Count);
            var target = scratch.Slice((runs.Row - runs.Top) * scratchWidth, scratchWidth).Slice(runs.First - origin.X, runs.Count);
            if (additive)
            {
                FadeOpaque(offsets, colors, under, target, runs.Count, key);
            }
            else
            {
                FadeColors(offsets, colors, under, target, runs.Count, key);
            }
        }

        var gamma = GetGamma(map, sprite);
        var destination = Rectangle.FromLTRB(origin.X, runs.Top, origin.X + FrameSize.Width, runs.End);
        var source = new Rectangle(0, 0, FrameSize.Width, rowCount);
        var graphics = map.Session.Graphics;
        graphics.SetAlphaBlend(BlendFactor.SourceAlpha, BlendFactor.One);
        new Texture(scratch, map.Session.Graphics.ScratchSize, VidPixelFormat.A4R4G4B4).DrawFlatQuad(screen, destination, source, gamma, graphics.Blend);
    }

    [EngineRoutine(0x004127e0)]
    internal override void SetGamma(Gamma gamma, int army, Gamma ambient, MyError? error) => StoreGamma(gamma, army, error);
}
