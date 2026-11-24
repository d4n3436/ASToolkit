using System.Drawing;
using System.Numerics;
using ASToolkit.Core;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a region sprite, an area that tiles a picture, draws fog and changes the definitions of the sprites inside it.
/// </summary>
public sealed class RegionSprite : Sprite
{
    /// <summary>
    /// Represents the number of definition pairs a region changes.
    /// </summary>
    public const int Pairs = 6;
    private const int FogPhaseMask = 7;
    private const int FogFrameStep = 2;
    private const float Half = 0.5f;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegionSprite"/> class.
    /// </summary>
    /// <param name="number">The number of the sprite.</param>
    /// <param name="definition">The definition of the sprite.</param>
    /// <param name="position">The position of the sprite.</param>
    /// <param name="army">The army of the sprite.</param>
    /// <param name="hitPoints">The hit points of the sprite.</param>
    /// <param name="direction">The direction of the sprite.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    public RegionSprite(int number, SpriteDefinition definition, Vector3 position, Army army, int hitPoints, Angle direction = default, Map? map = null)
        : base(number, definition, position, army, hitPoints, direction, map)
    {
        UnusedDefinition = map?.Definitions.Empty;
    }

    /// <summary>
    /// Gets or sets the width and height of the region.
    /// </summary>
    public Vector2 Size { get; set; }

    internal RegionFlags Flags { get; set; }

    internal int UnusedValue { get; set; }

    internal SpriteDefinition? UnusedDefinition { get; set; }

    internal int FogFar { get; set; }

    internal int FogNear { get; set; }

    internal uint FogColor { get; set; }

    internal int FogLevel { get; set; }

    internal int FogPhase { get; set; }

    internal ReadOnlyMemory<ushort> FogTable { get; set; }

    internal SpriteDefinition?[] From => field ??= new SpriteDefinition?[Pairs];

    internal SpriteDefinition?[] To => field ??= new SpriteDefinition?[Pairs];

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

        if (Definition != map.Definitions.Empty)
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
            var frameSize = Definition.FrameSize;

            if (frameSize is { Width: > 0, Height: > 0 })
            {
                for (float top = position.Y - half.Y; top < half.Y + position.Y; top += frameSize.Height)
                {
                    for (float left = position.X - half.X; left < half.X + position.X; left += frameSize.Width)
                    {
                        if (!Definition.Object.Properties.HasFlag(ObjectProperties.OnePhase))
                        {
                            Frame += tile * FogFrameStep;
                            tile++;
                            Frame %= Math.Max(1, (int)Definition.FileFrameCount);
                        }

                        Position = position with { X = frameSize.Center.X + left, Y = frameSize.Center.Y + top };
                        Definition.Draw(screen, map, this, camera);
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
}
