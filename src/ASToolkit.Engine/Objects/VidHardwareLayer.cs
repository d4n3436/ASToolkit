using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the shared data and frames of an object ID whose sprite file has the hardware layer flag.
/// </summary>
public sealed class VidHardwareLayer : Vid
{
    private const float TexelCenter = 0.5f;
    private const int ByteBits = 8;
    private const int HeightBias = 0x400;
    private const float DepthScale = 1.4143066f;
    private const float HeightScale = 0.125f;
    private const int DepthBias = 0x400;
    private const int PaletteSize = 0x400;

    /// <summary>
    /// Initializes a new instance of the <see cref="VidHardwareLayer"/> class from a sprite file.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <param name="gameObject">The object record.</param>
    /// <param name="weapons">The weapon table.</param>
    /// <param name="file">The sprite file.</param>
    /// <param name="totalFrames">The frame count of the sprite file.</param>
    public VidHardwareLayer(KnownObject id, in GameObject gameObject, AlienShooterWeaponRecord[] weapons, in VidResource file, int totalFrames)
        : base(id, gameObject, weapons, file, totalFrames)
    {
    }

    [EngineRoutine(0x0041b8f0)]
    private VidHardwareLayer(VidHardwareLayer source)
        : base(source)
    {
        Meshes = source.Meshes;
        Textures = source.Textures;
    }

    [EngineRoutine(0x004128a0)]
    internal override Vid CreateMirror() => new VidHardwareLayer(this);

    [EngineRoutine(0x0041b990)]
    internal override void Destruct(GameSession session)
    {
        if (NextMirror == this)
        {
            for (int frame = 0; frame < Textures.Length; frame++)
            {
                var texture = Textures[frame];
                if (texture is null || texture.IsEmpty)
                    continue;

                bool isRepeated = false;
                for (int earlier = 0; earlier < frame; earlier++)
                {
                    if (ReferenceEquals(Textures[earlier], Textures[frame]))
                    {
                        isRepeated = true;
                        break;
                    }
                }

                if (isRepeated)
                    continue;

                texture.Destruct(session);
            }

            Textures = [];
            Meshes = [];
        }

        base.Destruct(session);
    }

    /// <summary>
    /// Gets the mesh of each frame, in the order of the records of the sprite file.
    /// </summary>
    public HardwareLayerMesh[] Meshes { get; private set; } = [];

    /// <summary>
    /// Gets the texture of each frame, in the order of the records of the sprite file.
    /// </summary>
    public Texture?[] Textures { get; private set; } = [];

    /// <summary>
    /// Gets the pixel format of the frames' texels in the sprite file.
    /// </summary>
    public VidPixelFormat Format { get; private set; }

    /// <summary>
    /// Draws a sprite with the frames of this object ID as meshes placed by the position, scale and offset of the sprite.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="map">The map.</param>
    /// <param name="sprite">The sprite.</param>
    /// <param name="shift">The top left corner of the view, in map pixels.</param>
    [EngineRoutine(0x0041bdf0)]
    public override void Draw(Screen screen, Map map, Sprite sprite, Vector2 shift)
    {
        if (IsHidden)
            return;
        var position = sprite.Position;

        var viewport = screen.Viewport;

        uint seed = 0;
        bool hasDepth = FileType.HasFlag(VidType.ZBuffer);
        if (!Properties.HasFlag(ObjectProperties.AlwaysTop) && !hasDepth)
        {
            seed = (uint)BitConverter.SingleToInt32Bits(position.Z);
            float screenX = position.X - shift.X;
            float screenY = position.Y - position.Z - shift.Y;
            if (screenX < viewport.Left || screenX >= viewport.Right || screenY < viewport.Top || screenY >= viewport.Bottom || screen.Depth[screen.DepthWidth * (int)screenY + (int)screenX] > (int)position.Z * 8 + DepthBias)
                return;
        }

        float scaleX = Scale.X;
        float scaleY = Scale.Y;
        float scaleZ = Scale.Z;
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
            scaleX *= weapon.Interpolate(weapon.ScaleXKeys, (int)keyframe, keyframe);
            scaleY *= weapon.Interpolate(weapon.ScaleYKeys, (int)keyframe, keyframe);
            scaleZ *= weapon.Interpolate(weapon.ScaleHeightKeys, (int)keyframe, keyframe);
        }

        if (HasOffsetKeyframes)
        {
            x += weapon.Interpolate(weapon.OffsetXKeys, (int)keyframe, keyframe);
            y += weapon.Interpolate(weapon.OffsetYKeys, (int)keyframe, keyframe);
            z += weapon.Interpolate(weapon.OffsetZKeys, (int)keyframe, keyframe);
        }

        var world = new Matrix4x4(
            scaleX, 0f, 0f, 0f,
            -0f, scaleY, 0f, 0f,
            0f, 0f, scaleZ, 0f,
            x, y, z, 1f);
        var gamma = new Gamma(seed, 0).Add(sprite.GetGamma());
        if (!Properties.HasFlag(ObjectProperties.Gamma))
        {
            gamma = gamma.Add(map.Session.Graphics.Gamma);
        }

        var graphics = map.Session.Graphics;
        if (FileType.HasFlag(VidType.Blended))
        {
            graphics.SetAlphaBlend(BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha);
        }
        else
        {
            graphics.SetRenderState(RenderState.AlphaBlendEnable, 0);
        }

        var transform = world * Graphics.CreateView(shift, viewport) * map.Session.Graphics.Projection;
        Meshes[sprite.Frame].Draw(screen, Textures[sprite.Frame]!.View, transform, graphics.Blend, ~gamma.Negative);
    }

    /// <inheritdoc/>
    [EngineRoutine(0x0041bde0)]
    public override void SetLayer() => Layer = ObjectLayer.Hardware;

    [EngineRoutine(0x0041baf0)]
    internal override void Load(in VidResource file, ReadOnlyMemory<byte> data, GameSession session)
    {
        Format = file.Format;
        ReadOnlySpan<uint> palette = default;
        if (FileType.HasFlag(VidType.Palette))
        {
            if (file.Palette.IsEmpty)
            {
                Report(session.Error, LogError.Load, 0, "PAL "u8);
            }

            palette = MemoryMarshal.Cast<byte, uint>(file.Palette.Span[..Math.Min(file.Palette.Length, PaletteSize)]);
        }

        var records = file.FrameRecords;
        if (records.IsEmpty)
        {
            Report(session.Error, LogError.Load, 0, "DATA"u8);
        }

        int frameCount = Math.Min(FileFrameCount, records.Length);
        Textures = new Texture[frameCount];
        Meshes = new HardwareLayerMesh[frameCount];
        for (int frame = 0; frame < frameCount; frame++)
        {
            var record = records[frame].Span;
            ref readonly var header = ref MemoryMarshal.AsRef<VidLayerFrameHeader>(record);
            var size = new Size(header.Width & ushort.MaxValue, header.Height & ushort.MaxValue);
            int texelSize = Format switch
            {
                VidPixelFormat.R8G8B8 => 24,
                VidPixelFormat.A8R8G8B8 or VidPixelFormat.X8R8G8B8 => 32,
                VidPixelFormat.R5G6B5 or VidPixelFormat.X1R5G5B5 or VidPixelFormat.A1R5G5B5 or VidPixelFormat.A4R4G4B4 => 16,
                VidPixelFormat.P8 or VidPixelFormat.Dxt3 or VidPixelFormat.Dxt5 => 8,
                VidPixelFormat.Dxt1 => 4,
                _ => 0
            };
            int length = texelSize * size.Width * size.Height / ByteBits;
            int position = Unsafe.SizeOf<VidLayerFrameHeader>();
            var texels = records[frame].Slice(position, Math.Min(length, record.Length - position));
            position += texels.Length;

            var flags = TextureFlags.HasAlpha;
            if (FileType.HasFlag(VidType.Blended))
            {
                flags |= TextureFlags.AlphaPalette;
            }

            var texture = new Texture(texels.Span, size, Format, palette, flags, session);
            Textures[frame] = texture;
            size = texture.Size;
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
            Meshes[frame] = new HardwareLayerMesh(vertices, indexes);
        }
    }
}
