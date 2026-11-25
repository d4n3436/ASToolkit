using System.Drawing;

namespace ASToolkit.Engine;

internal sealed class DdDriver
{
    private readonly DisplayMode[] _modes;

    public DdDriver(DisplayMode[] modes)
    {
        _modes = modes;
    }

    public ReadOnlySpan<DisplayMode> Modes => _modes;

    [EngineRoutine(0x00434720)]
    public int GetMode(Size size, int bits)
    {
        var modes = Modes;
        for (int i = 0; i < modes.Length; i++)
        {
            if (modes[i].Size == size && Graphics.GetDisplayBits(modes[i].Format) == bits)
                return i;
        }

        return -1;
    }
}
