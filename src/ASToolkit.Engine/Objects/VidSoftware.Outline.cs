using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public partial class VidSoftware
{
    private const uint OutlineShadeColor = 0xa4a4a4;
    private const uint OutlineLightColor = 0x8f8f8f;

    private static readonly Size OutlineMargin = new(200, 100);
    private static readonly Vector2 OutlineSlant = new(0.35f, 0.7f);

    /// <inheritdoc/>
    [EngineRoutine(0x00418640)]
    public override void DrawOutline(Screen screen, Map map, Sprite sprite, Vector2 shift)
    {
        if (Frames.IsEmpty || FrameOffsets.Length == 0 || IsHidden)
            return;

        int halfWidth = FrameSize.Width / 2;
        int halfHeight = FrameSize.Height / 2;
        var origin = new Vector2(sprite.Position.X - shift.X - halfWidth, sprite.Position.Y - sprite.Position.Z - shift.Y - halfHeight);
        var viewport = screen.Viewport;
        if ((int)origin.X + FrameSize.Width + OutlineMargin.Width < viewport.Left || (int)origin.X >= viewport.Right)
            return;

        if ((int)origin.Y + FrameSize.Height + OutlineMargin.Height < viewport.Top || (int)origin.Y >= viewport.Bottom)
            return;

        origin.Y += sprite.Position.Z;
        float z = sprite.Position.Z;
        if (Properties.HasFlag(ObjectProperties.Wave))
        {
            z = new Angle((byte)(map.CurrentTime >> WaveTimeShift & WaveTableMask)).Sine * TopZ + z;
        }

        int record = FrameOffsets[sprite.Frame];
        int count = MemoryMarshal.AsRef<VidFrameHeader>(Frames[record..]).ExtraCount;
        if (count == 0)
            return;

        var points = MemoryMarshal.Cast<byte, VidOutlinePoint>(Frames[(record + Unsafe.SizeOf<VidFrameHeader>())..]);
        Span<Vector2> vertices = new Vector2[count * 2 + 2];
        for (int i = 0; i < count; i++)
        {
            ref readonly var point = ref points[i];
            float pointZ = point.Z + z;
            float baseX = point.X + origin.X;
            float baseY = point.Y + origin.Y;
            vertices[i * 2] = new Vector2(baseX, baseY - pointZ);
            vertices[i * 2 + 1] = new Vector2(pointZ * OutlineSlant.X + baseX, baseY - pointZ * OutlineSlant.Y);
        }

        vertices[count * 2] = vertices[0];
        vertices[count * 2 + 1] = vertices[1];
        var graphics = map.Session.Graphics;
        graphics.SetRenderState(RenderState.SpecularEnable, 0);
        graphics.SetAlphaBlend(BlendFactor.Zero, BlendFactor.SourceColor);
        ScreenPrimitives.FillTriangleStrip(screen, vertices, OutlineShadeColor, graphics.Blend.Source, graphics.Blend.Destination, CullMode.CounterClockwise);
        graphics.SetAlphaBlend(BlendFactor.DestinationColor, BlendFactor.One);
        ScreenPrimitives.FillTriangleStrip(screen, vertices, OutlineLightColor, graphics.Blend.Source, graphics.Blend.Destination, CullMode.Clockwise);
    }
}
