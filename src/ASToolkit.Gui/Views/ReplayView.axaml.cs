using System.Numerics;
using ASToolkit.Gui.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace ASToolkit.Gui.Views;

public partial class ReplayView : UserControl
{
    private const double ZoomStep = 0.1;
    private const double MinZoom = 0.1;
    private const double MaxZoom = 8;

    private Point? _dragStart;

    public ReplayView()
    {
        InitializeComponent();
        Viewport.SizeChanged += OnViewportSizeChanged;
    }

    private void OnViewportSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is not ReplayViewModel vm)
        {
            return;
        }

        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        vm.Viewport = new System.Drawing.Size(Math.Max(1, (int)(e.NewSize.Width * scaling)), Math.Max(1, (int)(e.NewSize.Height * scaling)));
    }

    private void OnWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not ReplayViewModel vm)
        {
            return;
        }

        vm.Zoom = Math.Clamp(vm.Zoom + Math.Sign(e.Delta.Y) * ZoomStep, MinZoom, MaxZoom);
        e.Handled = true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(Viewport).Properties.IsLeftButtonPressed)
        {
            _dragStart = e.GetPosition(Viewport);
            e.Pointer.Capture(Viewport);
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStart is null || DataContext is not ReplayViewModel vm)
        {
            return;
        }

        var position = e.GetPosition(Viewport);
        vm.Pan(new Vector2((float)(position.X - _dragStart.Value.X), (float)(position.Y - _dragStart.Value.Y)));
        _dragStart = position;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragStart = null;
        e.Pointer.Capture(null);
    }
}
