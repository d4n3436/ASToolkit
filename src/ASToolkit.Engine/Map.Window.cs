using System.Drawing;
using System.Globalization;
using ASToolkit.Engine.IO;

namespace ASToolkit.Engine;

public partial class Map
{
    private const int CommandIdMask = 0xFFFF;
    private const int AcceleratorSource = 1 << 16;
    private const string ScreenshotFolder = "Screens\\";
    private const string ScreenshotExtension = ".tga";
    private const string ScreenshotDateFormat = "yyyy-MM-dd";
    private const string ScreenshotTimeFormat = "HH:mm:ss";

    private static readonly Accelerator[] Accelerators =
    [
        new(AcceleratorFlags.VirtualKey | AcceleratorFlags.NoInvert, VirtualKey.F10, MenuCommand.About),
        new(AcceleratorFlags.VirtualKey | AcceleratorFlags.NoInvert, VirtualKey.F2, MenuCommand.Save),
        new(AcceleratorFlags.VirtualKey | AcceleratorFlags.NoInvert, VirtualKey.F3, MenuCommand.Load),
        new(AcceleratorFlags.VirtualKey | AcceleratorFlags.NoInvert | AcceleratorFlags.Shift | AcceleratorFlags.Last, VirtualKey.F9, MenuCommand.Screenshot)
    ];

    private bool TranslateAccelerator(in WindowMessage message)
    {
        if (message.Id is not (WindowMessageId.KeyDown or WindowMessageId.SysKeyDown))
            return false;

        foreach (var accelerator in Accelerators)
        {
            if (!accelerator.Flags.HasFlag(AcceleratorFlags.VirtualKey) || accelerator.Key != (VirtualKey)message.WParam ||
                accelerator.Flags.HasFlag(AcceleratorFlags.Shift) != Input.IsKeyHeld(VirtualKey.Shift) ||
                accelerator.Flags.HasFlag(AcceleratorFlags.Control) != Input.IsKeyHeld(VirtualKey.Control) ||
                accelerator.Flags.HasFlag(AcceleratorFlags.Alt) != Input.IsKeyHeld(VirtualKey.Alt))
                continue;

            DispatchMessage(new WindowMessage(WindowMessageId.Command, (int)accelerator.Command | AcceleratorSource, 0));

            return true;
        }

        return false;
    }

    [EngineRoutine(0x00404c30)]
    private void DispatchMessage(in WindowMessage message)
    {
        if (WindowProc(message))
            return;

        if (message.Id != WindowMessageId.Command)
        {
            DefWindowProc(message);

            return;
        }

        switch ((MenuCommand)(message.WParam & CommandIdMask))
        {
            case MenuCommand.Load when Constant.IsDebugMode:
                Load(GetOpenFileName().Span, WallTime);
                break;
            case MenuCommand.Exit:
                Input.PostMessage(WindowMessageId.Close, 0, 0);
                break;
            case MenuCommand.Screenshot:
                var now = Session.Time.GetLocalNow();
                string path = $"{ScreenshotFolder}{now.ToString(ScreenshotDateFormat, CultureInfo.InvariantCulture)} {now.ToString(ScreenshotTimeFormat, CultureInfo.InvariantCulture)}{ScreenshotExtension}";
                path = MyError.ReplaceFirst(MyError.ReplaceFirst(MyError.ReplaceFirst(path, ':', 'h'), ':', 'm'), ':', 's');
                Session.Graphics.SaveScreen(FileSystem, path, new Rectangle(Point.Empty, System.Drawing.Size.Truncate(Session.ScreenSize)));
                break;
        }
    }

    [EngineRoutine(0x0040c400)]
    private ReadOnlyMemory<byte> GetOpenFileName()
    {
        Session.Graphics.EnterMenuLoop();
        Session.Graphics.ExitMenuLoop();

        return default;
    }

    private void DefWindowProc(in WindowMessage message)
    {
        if (message.Id == WindowMessageId.Close)
        {
            WindowProc(new WindowMessage(WindowMessageId.Destroy, 0, 0));
        }
    }

    [EngineRoutine(0x0040b930)]
    private bool WindowProc(in WindowMessage message)
    {
        if (_flags.HasFlag(MapFlags.WindowActive) && Input.WindowProc(message.Id, message.WParam, message.LParam, Session.Graphics.WindowPosition, Session.Graphics.Viewport, Camera, CursorSprite))
            return true;

        switch (message.Id)
        {
            case WindowMessageId.EnterMenuLoop or WindowMessageId.EnterSizeMove:
                Session.Graphics.EnterMenuLoop();
                Sound.Pause(WallTime);
                break;
            case WindowMessageId.ExitMenuLoop or WindowMessageId.ExitSizeMove:
                Session.Graphics.ExitMenuLoop();
                Sound.Resume(WallTime);
                break;
            case WindowMessageId.ActivateApp:
                SetFlag(MapFlags.WindowActive, message.WParam != 0);

                if (_flags.HasFlag(MapFlags.WindowActive))
                {
                    Sound.Resume(WallTime);
                    CursorSprite.Enable(FileSystem);
                }
                else
                {
                    Sound.Pause(WallTime);
                    CursorSprite.Disable();
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
                Session.Error.Write("WM_PAINT"u8);
                break;
            case WindowMessageId.SysCommand when (SystemCommand)message.WParam is SystemCommand.Size or SystemCommand.Move or SystemCommand.Maximize or SystemCommand.MonitorPower:
                return Session.Graphics.Flags.HasFlag(GraphicsFlags.FullScreen);
        }

        return false;
    }
}
