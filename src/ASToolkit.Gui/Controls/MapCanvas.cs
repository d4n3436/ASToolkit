using System.Windows.Input;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Rendering;
using ASToolkit.Gui.Core;
using ASToolkit.Gui.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace ASToolkit.Gui.Controls;

/// <summary>
/// Shows an engine map that can be zoomed, dragged and animated, with the overlays of the level drawn over it.
/// </summary>
public sealed class MapCanvas : Control
{
    public static readonly StyledProperty<Map?> MapProperty = AvaloniaProperty.Register<MapCanvas, Map?>(nameof(Map));

    public static readonly StyledProperty<bool> AnimateProperty = AvaloniaProperty.Register<MapCanvas, bool>(nameof(Animate), true);

    public static readonly StyledProperty<MapOverlay?> OverlayProperty = AvaloniaProperty.Register<MapCanvas, MapOverlay?>(nameof(Overlay));

    public static readonly StyledProperty<bool> ShowHelpersProperty = AvaloniaProperty.Register<MapCanvas, bool>(nameof(ShowHelpers));

    public static readonly StyledProperty<bool> ShowScriptMarkersProperty = AvaloniaProperty.Register<MapCanvas, bool>(nameof(ShowScriptMarkers));

    public static readonly StyledProperty<bool> ShowRouteProperty = AvaloniaProperty.Register<MapCanvas, bool>(nameof(ShowRoute));

    public static readonly StyledProperty<ICommand?> HoverCommandProperty = AvaloniaProperty.Register<MapCanvas, ICommand?>(nameof(HoverCommand));

    public static readonly StyledProperty<ICommand?> SelectCommandProperty = AvaloniaProperty.Register<MapCanvas, ICommand?>(nameof(SelectCommand));

    private const double ZoomStep = 1.25;
    private const double MaxZoom = 16;
    private const double ClickDistance = 4;
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(40);

    private readonly DispatcherTimer _timer;
    private double _zoom = 1;
    private Point _offset;
    private Point? _pressed;
    private Point _lastPointer;
    private bool _attached;
    private bool _busy;
    private bool _isDue;
    private bool _isTickDue;
    private Bitmap? _image;
    private Rect _imageArea;

    public MapCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
        _timer = new DispatcherTimer { Interval = TickInterval };
        _timer.Tick += (_, _) =>
        {
            _isTickDue = true;
            RequestDraw();
        };
    }

    /// <summary>
    /// Gets or sets the map shown.
    /// </summary>
    public Map? Map
    {
        get => GetValue(MapProperty);
        set => SetValue(MapProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the map steps while it is shown, so its sprites animate.
    /// </summary>
    public bool Animate
    {
        get => GetValue(AnimateProperty);
        set => SetValue(AnimateProperty, value);
    }

    /// <summary>
    /// Gets or sets what is drawn over the map.
    /// </summary>
    public MapOverlay? Overlay
    {
        get => GetValue(OverlayProperty);
        set => SetValue(OverlayProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the sprites the game never shows are marked with circles.
    /// </summary>
    public bool ShowHelpers
    {
        get => GetValue(ShowHelpersProperty);
        set => SetValue(ShowHelpersProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the script events and their trigger areas are drawn.
    /// </summary>
    public bool ShowScriptMarkers
    {
        get => GetValue(ShowScriptMarkersProperty);
        set => SetValue(ShowScriptMarkersProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the tour or the quest order of the level is drawn.
    /// </summary>
    public bool ShowRoute
    {
        get => GetValue(ShowRouteProperty);
        set => SetValue(ShowRouteProperty, value);
    }

    /// <summary>
    /// Gets or sets the command run with the <see cref="MapPoint"/> under the pointer when it moves.
    /// </summary>
    public ICommand? HoverCommand
    {
        get => GetValue(HoverCommandProperty);
        set => SetValue(HoverCommandProperty, value);
    }

    /// <summary>
    /// Gets or sets the command run with the <see cref="MapPoint"/> that is clicked.
    /// </summary>
    public ICommand? SelectCommand
    {
        get => GetValue(SelectCommandProperty);
        set => SetValue(SelectCommandProperty, value);
    }

    /// <summary>
    /// Zooms and moves the view so the whole map fits.
    /// </summary>
    public void Fit()
    {
        if (Map is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var size = Map.Size;

        _zoom = Math.Min(Bounds.Width / size.Width, Bounds.Height / size.Height);
        _offset = new Point((size.Width - Bounds.Width / _zoom) / 2, (size.Height - Bounds.Height / _zoom) / 2);
        OnViewChanged();
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
        if (Map is null)
        {
            return;
        }

        if (_image is not null)
        {
            using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = (_zoom > 1 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality) }))
            {
                context.DrawImage(_image, new Rect(_image.Size), new Rect(ToView(_imageArea.TopLeft), ToView(_imageArea.BottomRight)));
            }
        }

        DrawOverlay(context);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MapProperty)
        {
            _image = null;
            Fit();
            UpdateTimer();
        }
        else if (change.Property == OverlayProperty || change.Property == ShowHelpersProperty || change.Property == ShowScriptMarkersProperty
                 || change.Property == ShowRouteProperty)
        {
            InvalidateVisual();
        }
        else if (change.Property == AnimateProperty)
        {
            UpdateTimer();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        UpdateTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        _timer.Stop();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (e.PreviousSize.Width <= 0 || e.PreviousSize.Height <= 0)
        {
            Fit();
        }

        OnViewChanged();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Map is null || e.Delta.Y == 0)
        {
            return;
        }

        var anchor = e.GetPosition(this);
        var mapAnchor = ToMap(anchor);
        var size = Map.Size;
        double fit = Math.Min(Bounds.Width / Math.Max(1, size.Width), Bounds.Height / Math.Max(1, size.Height));
        _zoom = Math.Clamp(e.Delta.Y > 0 ? _zoom * ZoomStep : _zoom / ZoomStep, Math.Min(fit / 2, 1), MaxZoom);
        _offset = new Point(mapAnchor.X - anchor.X / _zoom, mapAnchor.Y - anchor.Y / _zoom);
        OnViewChanged();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _pressed = e.GetPosition(this);
            _lastPointer = _pressed.Value;
            e.Pointer.Capture(this);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        if (_pressed is not null)
        {
            _offset -= (position - _lastPointer) / _zoom;
            _lastPointer = position;
            OnViewChanged();
        }

        RunCommand(HoverCommand, position);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_pressed is null)
        {
            return;
        }

        var pressed = _pressed.Value;
        var position = e.GetPosition(this);
        _pressed = null;
        e.Pointer.Capture(null);
        if (Math.Abs(position.X - pressed.X) + Math.Abs(position.Y - pressed.Y) < ClickDistance)
        {
            RunCommand(SelectCommand, position);
        }
    }

    private void RunCommand(ICommand? command, Point position)
    {
        var mapPosition = ToMap(position);
        var point = new MapPoint(new System.Drawing.Point((int)Math.Floor(mapPosition.X), (int)Math.Floor(mapPosition.Y)), _zoom);
        if (command?.CanExecute(point) == true)
        {
            command.Execute(point);
        }
    }

    private void DrawOverlay(DrawingContext context)
    {
        var overlay = Overlay;
        if (overlay is null)
        {
            return;
        }

        var painter = new OverlayPainter(context, point => ToView(new Point(point.X, point.Y)), new Rect(Bounds.Size), _zoom);
        if (ShowRoute)
        {
            painter.DrawRoute(overlay);
        }

        if (ShowScriptMarkers)
        {
            painter.DrawMarkers(overlay.Logic.Markers);
        }

        if (ShowHelpers)
        {
            painter.DrawHelpers(overlay.Helpers);
        }
    }

    private void UpdateTimer()
    {
        if (Animate && Map is not null && _attached)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    private void OnViewChanged()
    {
        InvalidateVisual();
        RequestDraw();
    }

    private async void RequestDraw()
    {
        _isDue = true;
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            while (_isDue)
            {
                _isDue = false;
                var map = Map;
                if (map is null || Bounds.Width <= 0 || Bounds.Height <= 0)
                {
                    break;
                }

                bool tick = _isTickDue;
                _isTickDue = false;
                var size = map.Size;
                var view = new Rect(_offset, Bounds.Size / _zoom).Intersect(new Rect(0, 0, size.Width, size.Height));
                if (view.Width < 1 || view.Height < 1)
                {
                    _image = null;
                    InvalidateVisual();
                    break;
                }

                var area = new System.Drawing.Rectangle((int)Math.Floor(view.X), (int)Math.Floor(view.Y),
                    (int)Math.Ceiling(view.Right) - (int)Math.Floor(view.X), (int)Math.Ceiling(view.Bottom) - (int)Math.Floor(view.Y));
                float scale = (float)Math.Min(1, _zoom * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1));
                var image = await Task.Run(() =>
                {
                    lock (map)
                    {
                        if (tick)
                        {
                            map.AdvanceClock();
                            map.Step();
                        }

                        using var drawn = MapImage.Render(map, area, scale);
                        return BitmapConversion.ToBitmap(drawn.Image);
                    }
                });

                if (map != Map)
                {
                    image.Dispose();
                    continue;
                }

                var previous = _image;
                _image = image;
                _imageArea = new Rect(area.X, area.Y, area.Width, area.Height);
                previous?.Dispose();
                InvalidateVisual();
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private Point ToView(Point point) => new((point.X - _offset.X) * _zoom, (point.Y - _offset.Y) * _zoom);

    private Point ToMap(Point point) => new(point.X / _zoom + _offset.X, point.Y / _zoom + _offset.Y);
}
