using ASToolkit.Gui.ViewModels;
using Avalonia;
using Avalonia.Controls;

namespace ASToolkit.Gui.Views;

public partial class ReplayView : UserControl
{
    private bool _isAttached;

    public ReplayView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        RequestFrame();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        (DataContext as ReplayViewModel)?.SilenceSound();
        base.OnDetachedFromVisualTree(e);
    }

    private void RequestFrame() => TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnFrame);

    private void OnFrame(TimeSpan time)
    {
        if (!_isAttached)
            return;

        (DataContext as ReplayViewModel)?.Tick();
        RequestFrame();
    }
}
