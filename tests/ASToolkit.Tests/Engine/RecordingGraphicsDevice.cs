using System.Drawing;
using ASToolkit.Engine;

namespace ASToolkit.Tests.Engine;

internal sealed class RecordingGraphicsDevice : IGraphicsDevice
{
    public int Scenes { get; private set; }

    public int EndedScenes { get; private set; }

    public List<Rectangle> Presented { get; } = [];

    public int BeginScene()
    {
        Scenes++;

        return 0;
    }

    public int EndScene()
    {
        EndedScenes++;

        return 0;
    }

    public int Present(Rectangle source, Rectangle destination)
    {
        Presented.Add(source);

        return 0;
    }
}
