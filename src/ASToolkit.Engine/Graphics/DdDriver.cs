using System.Drawing;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

internal sealed class DdDriver
{
    private const int MaxModes = 16;
    private const int TrueColorBits = 32;
    private const int HighColorBits = 16;

    private static readonly VidPixelFormat[] DepthFormats = [VidPixelFormat.D16, VidPixelFormat.D32, VidPixelFormat.D24X8, VidPixelFormat.D24S8];

    private readonly DisplayMode[] _modes = new DisplayMode[MaxModes];
    private readonly VidPixelFormat[] _depthFormats = new VidPixelFormat[MaxModes];
    private int _count;

    public ReadOnlySpan<DisplayMode> Modes => _modes.AsSpan(0, _count);

    [EngineRoutine(0x00402ca0)]
    public void FillParameters(ReadOnlySpan<DisplayMode> deviceModes, in GraphicsInit init, MyError error)
    {
        _count = 0;
        for (int i = deviceModes.Length - 1; i >= 0; i--)
        {
            var mode = deviceModes[i];
            bool isAccepted = mode.Format switch
            {
                VidPixelFormat.X8R8G8B8 or VidPixelFormat.A8R8G8B8 => init.HasMode(mode.Size, TrueColorBits),
                VidPixelFormat.X1R5G5B5 or VidPixelFormat.R5G6B5 => init.HasMode(mode.Size, HighColorBits),
                _ => false
            };

            if (!isAccepted || GetMode(mode.Size, Graphics.GetDisplayBits(mode.Format)) >= 0)
                continue;

            if (_count == MaxModes)
                throw new InvalidOperationException($"The display adapter has more than {MaxModes} accepted display modes.");

            _modes[_count] = mode;
            _depthFormats[_count] = VidPixelFormat.None;
            foreach (var depthFormat in DepthFormats)
            {
                if (Graphics.IsDepthStencilMatched(mode.Format, depthFormat))
                {
                    _depthFormats[_count] = depthFormat;
                    break;
                }
            }

            _count++;
            error.Log($"   Enum display modes {mode.Size.Width}x{mode.Size.Height} {MyError.Decode(Graphics.GetFormatName(mode.Format))} {MyError.Decode(Graphics.GetFormatName(_depthFormats[_count - 1]))}");
        }
    }

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
