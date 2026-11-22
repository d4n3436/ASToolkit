using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ASToolkit.Formats;
using ASToolkit.Engine.IO;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a definition whose sprite file has the hardware layer flag, which no shipped object uses.
/// </summary>
public sealed class HardwareLayerSpriteDefinition : SpriteDefinition
{
    private const float TexelCenter = 0.5f;
    private const int ByteBits = 8;
    private const int HeightBias = 0x400;
    private const float DepthScale = 1.4143066f;
    private const float HeightScale = 0.125f;
    private const int DepthBias = 0x400;

    /// <summary>
    /// Initializes a new instance of the <see cref="HardwareLayerSpriteDefinition"/> class from a sprite file.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <param name="gameObject">The object record.</param>
    /// <param name="weapon">The weapon record.</param>
    /// <param name="file">The sprite file.</param>
    /// <param name="totalFrames">The frame count of the definition's sprite.</param>
    public HardwareLayerSpriteDefinition(KnownObject id, in GameObject gameObject, in WeaponRecord weapon, in VidResource file, int totalFrames)
        : base(id, gameObject, weapon, file, totalFrames)
    {
    }

    /// <summary>
    /// Gets the frames, each a texture and a mesh, in the order of the records of the sprite file.
    /// </summary>
    public HardwareLayerFrame[] Frames { get; private set; } = [];

    /// <summary>
    /// Gets the palette of the frames' textures when the texels are palette indexes, as 256 entries of blue, green, red and alpha bytes.
    /// </summary>
    public ReadOnlyMemory<byte> Palette { get; private set; }

    [EngineRoutine(0x0041baf0)]
    internal override void Load(in VidResource file, ReadOnlyMemory<byte> data, GameLogger? logger)
    {
        if (FileType.HasFlag(VidType.Palette))
        {
            Palette = file.Palette;
        }

        var records = file.FrameRecords;

        Frames = new HardwareLayerFrame[Math.Min(FileFrameCount, records.Length)];
        for (int frame = 0; frame < Frames.Length; frame++)
        {
            var record = records[frame].Span;
            ref readonly var header = ref MemoryMarshal.AsRef<VidLayerFrameHeader>(record);
            var size = new Size(header.Width & ushort.MaxValue, header.Height & ushort.MaxValue);
            int length = GetTexelSize(file.Format) * size.Width * size.Height / ByteBits;
            int position = Unsafe.SizeOf<VidLayerFrameHeader>();
            var texels = records[frame].Slice(position, Math.Min(length, record.Length - position));
            position += texels.Length;

            ref readonly var mesh = ref MemoryMarshal.AsRef<VidLayerMeshHeader>(record[position..]);
            position += Unsafe.SizeOf<VidLayerMeshHeader>();
            var vertices = new HardwareLayerVertex[Math.Max(mesh.VertexCount, 0)];
            for (int i = 0; i < vertices.Length; i++)
            {
                ref readonly var vertex = ref MemoryMarshal.AsRef<VidLayerVertex>(record[position..]);
                position += Unsafe.SizeOf<VidLayerVertex>();
                vertices[i] = new HardwareLayerVertex(
                    new Vector3(vertex.X, vertex.Y * DepthScale, (vertex.Height - HeightBias) * HeightScale),
                    new Vector2((vertex.TextureX + TexelCenter) / size.Width, (vertex.TextureY + TexelCenter) / size.Height));
            }

            ushort[] indexes = new ushort[Math.Max(mesh.IndexCount, 0)];
            MemoryMarshal.Cast<byte, ushort>(record[position..])[..indexes.Length].CopyTo(indexes);
            ushort[] textures = Texture.Create(texels.Span, size, file.Format, Palette.Span, out var textureFormat);
            Frames[frame] = new HardwareLayerFrame(size, textureFormat, textures, vertices, indexes);
        }
    }

    /// <summary>
    /// Draws a sprite with the frames of this definition as meshes placed by the position, scale and offset of the sprite.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="map">The map.</param>
    /// <param name="sprite">The sprite.</param>
    /// <param name="camera">The top left corner of the view, in map pixels.</param>
    [EngineRoutine(0x0041bdf0)]
    public override void Draw(Screen screen, Map map, Sprite sprite, Vector2 camera)
    {
        if (IsHidden)
            return;
        var position = sprite.Position;

        var clip = screen.Clip;

        uint seed = 0;
        bool hasDepth = FileType.HasFlag(VidType.ZBuffer);
        if (!Object.Properties.HasFlag(ObjectProperties.AlwaysTop) && !hasDepth)
        {
            seed = (uint)BitConverter.SingleToInt32Bits(position.Z);
            float screenX = position.X - camera.X;
            float screenY = position.Y - position.Z - camera.Y;
            if (screenX < clip.Left || screenX >= clip.Right || screenY < clip.Top || screenY >= clip.Bottom || screen.Depth[screen.DepthWidth * (int)screenY + (int)screenX] > (int)position.Z * 8 + DepthBias)
                return;
        }

        float scaleX = Object.Scale.X;
        float scaleY = Object.Scale.Y;
        float scaleZ = Object.Scale.Z;
        float x = position.X;
        float y = position.Y;
        float z = position.Z;
        ref readonly var weapon = ref Weapon;
        float keyframe = sprite.KeyframePosition;
        if (HasScaleKeyframes || HasOffsetKeyframes)
        {
            seed = (uint)(int)keyframe;
        }

        if (HasScaleKeyframes)
        {
            scaleX *= weapon.Interpolate(weapon.ScaleXKeys, keyframe);
            scaleY *= weapon.Interpolate(weapon.ScaleYKeys, keyframe);
            scaleZ *= weapon.Interpolate(weapon.ScaleHeightKeys, keyframe);
        }

        if (HasOffsetKeyframes)
        {
            x += weapon.Interpolate(weapon.OffsetXKeys, keyframe);
            y += weapon.Interpolate(weapon.OffsetYKeys, keyframe);
            z += weapon.Interpolate(weapon.OffsetZKeys, keyframe);
        }

        var world = new Matrix4x4(
            scaleX, 0f, 0f, 0f,
            -0f, scaleY, 0f, 0f,
            0f, 0f, scaleZ, 0f,
            x, y, z, 1f);
        var gamma = new Gamma(seed, 0).Add(sprite.GetGamma());
        if (!Object.Properties.HasFlag(ObjectProperties.Gamma))
        {
            gamma = gamma.Add(map.Session.Graphics.Gamma);
        }

        if (sprite.Frame < 0 || sprite.Frame >= Frames.Length)
            return;

        var blend = FileType.HasFlag(VidType.Blended) ? QuadBlend.Alpha : QuadBlend.None;
        var transform = world * Graphics.CreateView(camera, clip) * Graphics.CreateProjection(clip);
        Frames[sprite.Frame].Draw(screen, transform, blend, ~gamma.Negative);
    }

    /// <inheritdoc/>
    [EngineRoutine(0x0041bde0)]
    public override void SetLayer() => Layer = ObjectLayer.Hardware;

    private static int GetTexelSize(VidPixelFormat format) => format switch
    {
        VidPixelFormat.R8G8B8 => 24,
        VidPixelFormat.A8R8G8B8 or VidPixelFormat.X8R8G8B8 => 32,
        VidPixelFormat.R5G6B5 or VidPixelFormat.X1R5G5B5 or VidPixelFormat.A1R5G5B5 or VidPixelFormat.A4R4G4B4 => 16,
        VidPixelFormat.P8 or VidPixelFormat.Dxt3 or VidPixelFormat.Dxt5 => 8,
        VidPixelFormat.Dxt1 => 4,
        _ => 0
    };
}
