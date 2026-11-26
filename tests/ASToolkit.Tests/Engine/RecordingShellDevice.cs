using ASToolkit.Engine.IO;

namespace ASToolkit.Tests.Engine;

internal sealed class RecordingShellDevice : IShellDevice
{
    public List<(string File, string Parameters)> Launches { get; } = [];

    public void Execute(string file, string parameters) => Launches.Add((file, parameters));
}
