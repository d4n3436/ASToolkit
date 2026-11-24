using System.Drawing;
using System.Numerics;
using ASToolkit.Core;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a region sprite, an area that tiles a picture, draws fog and changes the object IDs of the sprites inside it.
/// </summary>
public sealed class Region : Sprite
{
    /// <summary>
    /// Represents the number of object ID pairs a region changes.
    /// </summary>
    public const int Pairs = 6;
    private const int FloatRegionSizeVersion = 10;
    private const int FogPhaseMask = 7;
    private const int FogRampShift = 3;
    private const int FogStepsPerUnit = 8;
    private const int FogFrameStep = 2;
    private const float Half = 0.5f;
    private const float RegionBoxMargin = 1;

    /// <summary>
    /// Initializes a new instance of the <see cref="Region"/> class on a map.
    /// </summary>
    /// <param name="vid">The shared data and frames of the sprite's object ID.</param>
    /// <param name="position">The position.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="parent">The sprite that the new sprite takes its army and height from, or null.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    [EngineRoutine(0x00449640)]
    public Region(Vid vid, Vector3 position, Angle direction, Sprite? parent, Map map)
        : base(vid, position, direction, parent, map)
    {
        UnusedVid = map.Vids.Empty;
    }

    /// <summary>
    /// Gets or sets the width and height of the region.
    /// </summary>
    public Vector2 Size { get; set; }

    internal RegionFlags Flags { get; set; }

    internal int UnusedValue { get; set; }

    internal Vid? UnusedVid { get; set; }

    internal int FogFar { get; set; }

    internal int FogNear { get; set; }

    internal uint FogColor { get; set; }

    internal int FogLevel { get; set; }

    internal int FogPhase { get; set; }

    internal ReadOnlyMemory<ushort> FogTable { get; set; }

    internal Vid?[] From => field ??= new Vid?[Pairs];

    internal Vid?[] To => field ??= new Vid?[Pairs];

    /// <summary>
    /// Draws the region's picture tiled over its area, then its fog, and advances the fog's level.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="map">The map.</param>
    /// <param name="camera">The top left corner of the view, in map pixels.</param>
    [EngineRoutine(0x00449810)]
    public override void Draw(Screen screen, Map map, Vector2 camera)
    {
        var position = Position;
        int frame = Frame;
        var half = Size * Half;

        if (Vid != map.Vids.Empty)
        {
            var savedViewport = screen.Viewport;

            if (!Flags.HasFlag(RegionFlags.CoversMap))
            {
                float groundY = position.Y - position.Z;
                screen.Viewport = Rectangle.FromLTRB(
                    (int)(position.X - half.X - camera.X),
                    (int)(groundY - half.Y - camera.Y),
                    (int)(half.X + position.X - camera.X),
                    (int)(half.Y + groundY - camera.Y));
            }

            int tile = 0;
            var frameSize = Vid.FrameSize;

            if (frameSize is { Width: > 0, Height: > 0 })
            {
                for (float top = position.Y - half.Y; top < half.Y + position.Y; top += frameSize.Height)
                {
                    for (float left = position.X - half.X; left < half.X + position.X; left += frameSize.Width)
                    {
                        if (!Vid.Object.Properties.HasFlag(ObjectProperties.OnePhase))
                        {
                            Frame += tile * FogFrameStep;
                            tile++;
                            Frame %= Math.Max(1, (int)Vid.FileFrameCount);
                        }

                        Position = position with { X = frameSize.Center.X + left, Y = frameSize.Center.Y + top };
                        Vid.Draw(screen, map, this, camera);
                    }
                }
            }

            if (!Flags.HasFlag(RegionFlags.CoversMap))
            {
                screen.Viewport = savedViewport;
            }
        }

        Position = position;
        Frame = frame;

        if (FogFar <= FogNear)
        {
            FogLevel = 0;

            return;
        }

        if (!Flags.HasFlag(RegionFlags.AnimatedFog))
        {
            FogLevel = FogFar * Graphics.FogDepthPerLevel;
        }
        else
        {
            int phase = map.Time & FogPhaseMask;

            if ((uint)phase < (uint)FogPhase)
            {
                int target = FogFar * Graphics.FogDepthPerLevel;

                if (FogLevel < target)
                {
                    FogLevel += FogFrameStep;
                }
                else if (target < FogLevel)
                {
                    FogLevel = 0;
                }
            }

            FogPhase = phase;
        }

        if (Flags.HasFlag(RegionFlags.CoversMap))
        {
            map.Session.Graphics.DrawFog(screen, new Vector2(screen.Viewport.Left, screen.Viewport.Top), new Vector2(screen.Viewport.Right, screen.Viewport.Bottom), FogNear, FogFar, FogColor, FogTable.Span, FogLevel, Flags.HasFlag(RegionFlags.LightFog));
            return;
        }

        float centerY = position.Y - position.Z;
        var topLeft = new Vector2(position.X - half.X - camera.X, centerY - half.Y - camera.Y);
        var bottomRight = new Vector2(half.X + position.X - camera.X, half.Y + centerY - camera.Y);
        map.Session.Graphics.DrawFog(screen, topLeft, bottomRight, FogNear, FogFar, FogColor, FogTable.Span, FogLevel, Flags.HasFlag(RegionFlags.LightFog));
    }

    [EngineRoutine(0x00449700)]
    internal override void Destruct()
    {
        Map.DeletePointerToSprite(this);
        base.Destruct();
    }

    [EngineRoutine(0x00449730)]
    internal override void DrawSecondaryInfo()
    {
        var position = Position;
        var size = Size;
        float left = position.X - size.X * Map.HalfShare - Map.Camera.X;
        float top = position.Y - position.Z - size.Y * Map.HalfShare - Map.Camera.Y;
        float right = size.X * Map.HalfShare + position.X - Map.Camera.X;
        float bottom = size.Y * Map.HalfShare + (position.Y - position.Z) - Map.Camera.Y;
        Map.Session.Graphics.DrawRectangle(Map, new Vector2(left - RegionBoxMargin, top - RegionBoxMargin), new Vector2(right + RegionBoxMargin, bottom + RegionBoxMargin), Map.White);
    }

    [EngineRoutine(0x00449d00)]
    private void SetFogParameters(int near, int far, uint color, ReadOnlySpan<ushort> ramp, bool isPalettized)
    {
        FogFar = far;
        FogNear = near;
        FogColor = color;
        FogTable = default;

        if (near >= far)
            return;

        int span = far - near;
        ushort[] table = new ushort[span * FogStepsPerUnit + 1];

        for (int step = span * FogStepsPerUnit; step >= 0; step--)
        {
            int index = step * byte.MaxValue / span >> FogRampShift;
            table[step] = isPalettized ? (ushort)index : ramp[index];
        }

        FogTable = table;
    }

    [EngineRoutine(0x00449e20)]
    internal override int Action(ActionCode act, ref Resource stream, int version)
    {
        if (act is not (ActionCode.Restore or ActionCode.RestoreOld))
            return base.Action(act, ref stream, version);

        base.Action(act, ref stream, version);
        var fog = default(RegionFog);
        stream.ReadInto(ref fog);
        Flags = fog.Flags;
        SetFogParameters(fog.Near, fog.Far, fog.Color, Map.Session.Graphics.ColorTable, (Map.Session.Graphics.Screen?.TextureFormat ?? Map.Session.Graphics.Buffers.LightFormat) == VidPixelFormat.P8);
        if (version < FloatRegionSizeVersion)
        {
            var size = default(Size);
            stream.ReadInto(ref size);
            Size = new Vector2(size.Width, size.Height);
        }
        else
        {
            var size = default(Vector2);
            stream.ReadInto(ref size);
            Size = size;
        }

        UnusedValue = stream.Read<int>();
        UnusedVid = Map.Vids.Find((KnownObject)stream.Read<int>()) ?? Map.Vids.Empty;
        for (int pair = 0; pair < Pairs; pair++)
        {
            var replacement = new RegionReplacement(-1, -1);
            stream.ReadInto(ref replacement);
            var fromVid = Map.Vids.Find((KnownObject)replacement.From);
            From[pair] = fromVid;
            To[pair] = Map.Vids.Find((KnownObject)replacement.To);
            fromVid?.IsRegionReplaced = true;
        }

        return 0;
    }
}
