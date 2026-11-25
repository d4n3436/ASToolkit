using System.Drawing;
using ASToolkit.Bot;
using ASToolkit.Bot.Rendering;
using ASToolkit.Engine;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class DrawingTests
{
    private const string MapName = "Level_07.map";
    private const int Frames = 20;

    [Fact]
    public void Step_WithAScreenAndARenderBetweenFrames_ChangesNothingButTheScreen()
    {
        var drawn = Load();
        var plain = Load();
        drawn.Session.Graphics.Screen = new Screen(Size.Round(drawn.Session.Graphics.ScreenSize), true);

        Step(drawn, Frames / 2);
        var view = new Rectangle(Point.Round(new PointF(drawn.Shift.X, drawn.Shift.Y)), Size.Round(drawn.Session.Graphics.ScreenSize));
        using (MapImage.Render(drawn, view, 1f))
        {
        }

        Step(drawn, Frames / 2);
        Step(plain, Frames);

        Assert.Equal(Snapshot(plain), Snapshot(drawn));
        Assert.Contains(drawn.Session.Graphics.Screen.TrueColorPixels.ToArray(), pixel => (pixel & 0xffffff) != 0);
    }

    private static Map Load()
    {
        string game = TestGameFolder.RequireFolder();
        return Playthrough.LoadMap(game, TestGameFolder.Objects, Path.Combine(game, "Maps", MapName));
    }

    private static void Step(Map map, int frames)
    {
        for (int frame = 0; frame < frames; frame++)
        {
            map.Step();
        }
    }

    private static string Snapshot(Map map)
    {
        var lines = new List<string> { $"time {map.CurrentTime} random {map.RandomState} camera {map.Shift}" };
        for (int layer = 0; layer < Map.LayerCount; layer++)
        {
            foreach (var sprite in map.GetLayer(layer))
            {
                if (sprite is not null)
                {
                    lines.Add($"{layer} {(int)sprite.Vid.Id} {sprite.Position} {sprite.Frame} {sprite.Hp} {sprite.Animation} {sprite.IsInvisible}");
                }
            }
        }

        return string.Join('\n', lines);
    }
}
