using System.Drawing;
using System.Numerics;
using ASToolkit.Core.Engine;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class ScrollCameraTests
{
    [Fact]
    public void Update_StillPlayerAndMouse_SettlesHalfwayBetweenThem()
    {
        var screen = new SizeF(1024, 768);
        var camera = ScrollCamera.Start(screen, new SizeF(10000, 10000), new Vector2(3000, 3000));
        var player = new Vector3(5000, 5000, 0);
        var mouse = new Vector2(400, 300);
        int[] frameTimes = [16, 17, 17];
        for (int frame = 0; frame < 600; frame++)
        {
            camera = camera.Update(player, camera.Position + mouse, frameTimes[frame % frameTimes.Length]);
        }

        Assert.InRange(camera.Position.X, player.X + mouse.X - screen.Width - 30, player.X + mouse.X - screen.Width + 30);
        Assert.InRange(camera.Position.Y, player.Y + mouse.Y - screen.Height - 30, player.Y + mouse.Y - screen.Height + 30);
    }

    [Fact]
    public void Update_PlayerAtTheMapCorner_KeepsTheScreenOnTheMap()
    {
        var camera = ScrollCamera.Start(new SizeF(1024, 768), new SizeF(4000, 3000), new Vector2(-50, -50));

        camera = camera.Update(new Vector3(10, 10, 0), new Vector2(0, 0), 17);

        Assert.Equal(Vector2.Zero, camera.Position);
    }
}
