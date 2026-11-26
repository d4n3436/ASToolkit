using System.Drawing;
using ASToolkit.Engine.IO;

namespace ASToolkit.Engine;

public partial class Map
{
    private const int CommandIdMask = 0xFFFF;
    private const int AcceleratorSource = 1 << 16;
    private const string ScreenshotFolder = "Screens\\";
    private const string ScreenshotExtension = ".tga";

    private static readonly Accelerator[] Accelerators =
    [
        new(AcceleratorFlags.VirtualKey | AcceleratorFlags.NoInvert, VirtualKey.F10, MenuCommand.About),
        new(AcceleratorFlags.VirtualKey | AcceleratorFlags.NoInvert, VirtualKey.F2, MenuCommand.Save),
        new(AcceleratorFlags.VirtualKey | AcceleratorFlags.NoInvert, VirtualKey.F3, MenuCommand.Load),
        new(AcceleratorFlags.VirtualKey | AcceleratorFlags.NoInvert | AcceleratorFlags.Shift | AcceleratorFlags.Last, VirtualKey.F9, MenuCommand.Screenshot)
    ];

    [EngineRoutine(0x00404c30)]
    private void DispatchMessage(in WindowMessage message)
    {
        if (WindowProc(message))
            return;

        if (message.Id != WindowMessageId.Command)
        {
            if (message.Id == WindowMessageId.Close)
            {
                WindowProc(new WindowMessage(WindowMessageId.Destroy, 0, 0));
            }

            return;
        }

        switch ((MenuCommand)(message.WParam & CommandIdMask))
        {
            case MenuCommand.Load when Constant.IsDebugMode:
                Load(OpenSaveDialog().Span);
                break;
            case MenuCommand.Exit:
                Input.PostMessage(WindowMessageId.Close, 0, 0);
                break;
            case MenuCommand.Screenshot:
                string path = $"{ScreenshotFolder}{MyLib.GetDateString(Session.Time)} {MyLib.GetTimeString(Session.Time)}{ScreenshotExtension}";
                path = path.ReplaceFirst(":", "h").ReplaceFirst(":", "").ReplaceFirst(":", "");
                Session.Graphics.SaveScreen(path, new Rectangle(Point.Empty, System.Drawing.Size.Truncate(Session.Graphics.ScreenSize)));
                break;
        }
    }

    [EngineRoutine(0x0040b930)]
    private bool WindowProc(in WindowMessage message)
    {
        if (_flags.HasFlag(MapFlags.WindowActive) && Input.WindowProc(message.Id, message.WParam, message.LParam))
            return true;

        switch (message.Id)
        {
            case WindowMessageId.EnterMenuLoop or WindowMessageId.EnterSizeMove:
                Session.Graphics.BeginPause();
                Sound.Pause();
                break;
            case WindowMessageId.ExitMenuLoop or WindowMessageId.ExitSizeMove:
                Session.Graphics.EndPause();
                Sound.Resume();
                break;
            case WindowMessageId.ActivateApp:
                SetFlag(MapFlags.WindowActive, message.WParam != 0);

                if (_flags.HasFlag(MapFlags.WindowActive))
                {
                    Sound.Resume();
                    Mouse.Enable();
                }
                else
                {
                    Sound.Pause();
                    Mouse.Disable();
                }

                break;
            case WindowMessageId.Destroy:
                if (!Session.Graphics.Flags.HasFlag(GraphicsFlags.FullScreen))
                {
                    var position = Session.Graphics.WindowPosition;
                    Registry.SetNumber("WindowPositionX", position.X);
                    Registry.SetNumber("WindowPositionY", position.Y);
                }

                _hasWindow = false;
                Input.PostQuitMessage();
                break;
            case WindowMessageId.Paint:
                Session.Error.Log("WM_PAINT"u8);
                break;
            case WindowMessageId.SysCommand when (SystemCommand)message.WParam is SystemCommand.Size or SystemCommand.Move or SystemCommand.Maximize or SystemCommand.MonitorPower:
                return Session.Graphics.Flags.HasFlag(GraphicsFlags.FullScreen);
        }

        return false;
    }

    [EngineRoutine(0x0040c400)]
    private ReadOnlyMemory<byte> OpenSaveDialog()
    {
        Session.Graphics.BeginPause();
        Session.Graphics.EndPause();

        return default;
    }
}
