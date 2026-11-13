using System.Numerics;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Rendering;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ASToolkit.Tests.Rendering;

public class ReplayRenderingTests
{
    private const int SampleTime = 600;
    private const uint BlackoutTint = 0x606060;

    [Fact]
    public void Render_CollectedPickup_HidesItsGlowToo()
    {
        var renderer = new MapRenderer(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var floor = new MergedMapSprite(1, KnownObject.BasementFloor, new Vector3(160, 160, 0), 0, 0, []);
        var collected = new MergedMapSprite(2, KnownObject.Health, new Vector3(120, 160, 0), 0, 0, []);
        var remaining = collected with { Handle = 3, Position = new Vector3(200, 160, 0) };
        var scene = renderer.CreateScene([floor, collected, remaining]);
        Assert.Contains(scene.Placed, placement => placement.Source is not null && placement.Source.Id == KnownObject.AmmoGlow &&
            placement.Source.Sprite == collected);

        var area = new Rectangle(80, 100, 160, 100);
        var visible = Render(scene, area);
        var after = Render(scene, area, new HashSet<MergedMapSprite> { collected });
        var expected = Render(renderer.CreateScene([floor, remaining]), area);
        Assert.False(visible.SequenceEqual(after));
        Assert.Equal(expected, after);
        Assert.Equal(visible, Render(scene, area));
    }

    [Fact]
    public void Render_ViewerAndReplay_UseTheSameInitialMapLighting()
    {
        string game = TestGameFolder.RequireFolder();
        var renderer = new MapRenderer(game, TestGameFolder.Objects) { MatchGameLighting = true };
        var graphics = MapResourceBuilder.Load(Path.Combine(game, "Maps", "ADDON", "Level_01.map")).Graphics;
        var floor = new MergedMapSprite(1, KnownObject.BasementFloor, new Vector3(160, 160, 0), 0, 0, []);
        var scene = renderer.CreateScene([floor], graphics);
        var area = new Rectangle(120, 120, 80, 80);
        var viewer = Render(scene, area);

        renderer.UpdateLive(scene, []);
        Assert.Equal(viewer, Render(scene, area, negative: graphics.AmbientNegative));
        Assert.False(viewer.SequenceEqual(Render(scene, area, negative: 0)));
        Assert.Equal(Render(renderer.CreateScene([floor]), area), Render(scene, area, negative: 0));
    }

    [Theory]
    [InlineData(KnownObject.BasementWall2Slash)]
    [InlineData(KnownObject.Container2Alt)]
    public void Render_Darkened_PreservesTheObjectsTintedColors(KnownObject id)
    {
        var renderer = new MapRenderer(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var placement = new MergedMapSprite(1, id, new Vector3(160, 160, 0), 0, 0, []);
        var scene = renderer.CreateScene([placement]);
        var area = new Rectangle(scene.Left, scene.Top, scene.Width, scene.Height);
        var original = Render(scene, area);
        var darkened = Render(scene, area, negative: 0x808080);
        var expected = original.Select(color => new Bgra32((byte)(color.R / 2), (byte)(color.G / 2), (byte)(color.B / 2))).ToArray();

        Assert.Contains(original, color => color.R > 0);
        Assert.Equal(expected, darkened);
    }

    [Fact]
    public void Render_Blackout_TintsLitPalettesAndPreservesUnlitPalettes()
    {
        var renderer = new MapRenderer(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var floor = new MergedMapSprite(1, KnownObject.BasementFloor, new Vector3(160, 160, 0), 0, 0, []);
        var pickup = floor with { Id = KnownObject.Health };
        var area = new Rectangle(120, 120, 80, 80);

        var lit = renderer.CreateScene([floor]);
        Assert.False(Render(lit, area).SequenceEqual(Render(lit, area, negative: BlackoutTint)));
        var unlit = renderer.CreateScene([pickup]);
        Assert.Contains(unlit.Placed, placement => placement.Frame?.OriginalPalette is not null);
        Assert.Equal(Render(unlit, area), Render(unlit, area, negative: BlackoutTint));
    }

    private static Bgra32[] Render(MapScene scene, Rectangle world, uint? negative = null) => Render(scene, world, new HashSet<MergedMapSprite>(), negative);

    private static Bgra32[] Render(MapScene scene, Rectangle world, IReadOnlySet<MergedMapSprite> hidden, uint? negative = null)
    {
        var pixels = new Bgra32[world.Width * world.Height];
        world.Offset(-scene.Left, -scene.Top);
        scene.Render(pixels, world.Width, world, 1, SampleTime, hidden, negative, null);
        return pixels;
    }
}
