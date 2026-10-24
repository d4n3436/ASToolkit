using System.Diagnostics;
using System.Windows.Input;
using ASToolkit.Core.Rendering;
using ASToolkit.Gui.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SixLabors.ImageSharp.PixelFormats;
using Rectangle = SixLabors.ImageSharp.Rectangle;

namespace ASToolkit.Gui.Controls;

/// <summary>
/// Represents a control that shows a map scene with zoom, panning and animation.
/// </summary>
public sealed class MapCanvas : Control
{
    public static readonly StyledProperty<MapScene?> SceneProperty = AvaloniaProperty.Register<MapCanvas, MapScene?>(nameof(Scene));

    public static readonly StyledProperty<bool> AnimatesProperty = AvaloniaProperty.Register<MapCanvas, bool>(nameof(Animates), true);

    public static readonly StyledProperty<MapOverlay?> OverlayProperty = AvaloniaProperty.Register<MapCanvas, MapOverlay?>(nameof(Overlay));

    public static readonly StyledProperty<bool> ShowsHelpersProperty = AvaloniaProperty.Register<MapCanvas, bool>(nameof(ShowsHelpers));

    public static readonly StyledProperty<bool> ShowsScriptMarkersProperty = AvaloniaProperty.Register<MapCanvas, bool>(nameof(ShowsScriptMarkers));

    public static readonly StyledProperty<bool> ShowsRouteProperty = AvaloniaProperty.Register<MapCanvas, bool>(nameof(ShowsRoute));

    public static readonly StyledProperty<ICommand?> HoverCommandProperty = AvaloniaProperty.Register<MapCanvas, ICommand?>(nameof(HoverCommand));

    public static readonly StyledProperty<ICommand?> SelectCommandProperty = AvaloniaProperty.Register<MapCanvas, ICommand?>(nameof(SelectCommand));

    private const int TileSize = 512;
    private const int MaxTiles = 160;
    private const double ZoomStep = 1.25;
    private const double MaxZoom = 16;
    private const double ClickDistance = 4;
    private const double PatchShare = 0.4;
    private const int MaxAnimatedLevel = 3;
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(40);

    private readonly Dictionary<TileKey, Tile> _tiles = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer;
    private double _zoom = 1;
    private Point _offset;
    private Point? _pressed;
    private Point _lastPointer;
    private bool _busy;
    private bool _attached;
    private bool _tickDue;
    private long _frameNumber;

    public MapCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
        _timer = new DispatcherTimer { Interval = TickInterval };
        _timer.Tick += (_, _) =>
        {
            _tickDue = true;
            Pump();
        };
    }

    /// <summary>
    /// Gets or sets the scene shown.
    /// </summary>
    public MapScene? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the objects of the scene animate.
    /// </summary>
    public bool Animates
    {
        get => GetValue(AnimatesProperty);
        set => SetValue(AnimatesProperty, value);
    }

    /// <summary>
    /// Gets or sets what is drawn over the scene.
    /// </summary>
    public MapOverlay? Overlay
    {
        get => GetValue(OverlayProperty);
        set => SetValue(OverlayProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the sprites the game never shows are marked with circles.
    /// </summary>
    public bool ShowsHelpers
    {
        get => GetValue(ShowsHelpersProperty);
        set => SetValue(ShowsHelpersProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the script events and their trigger areas are drawn.
    /// </summary>
    public bool ShowsScriptMarkers
    {
        get => GetValue(ShowsScriptMarkersProperty);
        set => SetValue(ShowsScriptMarkersProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the tour or the quest order of the level is drawn.
    /// </summary>
    public bool ShowsRoute
    {
        get => GetValue(ShowsRouteProperty);
        set => SetValue(ShowsRouteProperty, value);
    }

    /// <summary>
    /// Gets or sets the command run with the <see cref="ScenePoint"/> under the pointer when it moves.
    /// </summary>
    public ICommand? HoverCommand
    {
        get => GetValue(HoverCommandProperty);
        set => SetValue(HoverCommandProperty, value);
    }

    /// <summary>
    /// Gets or sets the command run with the <see cref="ScenePoint"/> that is clicked.
    /// </summary>
    public ICommand? SelectCommand
    {
        get => GetValue(SelectCommandProperty);
        set => SetValue(SelectCommandProperty, value);
    }

    private int? Time => Animates ? (int)_clock.ElapsedMilliseconds : null;

    /// <summary>
    /// Zooms and moves the view so the whole scene fits.
    /// </summary>
    public void Fit()
    {
        var scene = Scene;
        if (scene is null || scene.IsEmpty || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        _zoom = Math.Min(Bounds.Width / scene.Width, Bounds.Height / scene.Height);
        _offset = new Point((scene.Width - Bounds.Width / _zoom) / 2, (scene.Height - Bounds.Height / _zoom) / 2);
        OnViewChanged();
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
        var scene = Scene;
        if (scene is null || scene.IsEmpty)
        {
            return;
        }

        _frameNumber++;
        int level = GetLevel();
        var mode = _zoom > GetScale(level) ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality;
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = mode }))
        {
            for (int coarser = GetMaxLevel(scene); coarser >= level; coarser--)
            {
                foreach (var key in GetVisibleTiles(scene, coarser))
                {
                    if (_tiles.TryGetValue(key, out var tile) && tile.Bitmap is not null)
                    {
                        tile.LastDrawn = _frameNumber;
                        context.DrawImage(tile.Bitmap, new Rect(0, 0, tile.Area.Width, tile.Area.Height), ToView(tile.Area, GetScale(coarser)));
                    }
                }
            }
        }

        DrawOverlay(context, scene);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SceneProperty)
        {
            ClearTiles();
            Fit();
            UpdateTimer();
            InvalidateVisual();
            Pump();
        }
        else if (change.Property == OverlayProperty || change.Property == ShowsHelpersProperty || change.Property == ShowsScriptMarkersProperty
                 || change.Property == ShowsRouteProperty)
        {
            InvalidateVisual();
        }
        else if (change.Property == AnimatesProperty)
        {
            ClearTiles();
            UpdateTimer();
            InvalidateVisual();
            Pump();
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
        if (Scene is null || e.Delta.Y == 0)
        {
            return;
        }

        var anchor = e.GetPosition(this);
        var sceneAnchor = ToScene(anchor);
        double fit = Math.Min(Bounds.Width / Math.Max(1, Scene.Width), Bounds.Height / Math.Max(1, Scene.Height));
        _zoom = Math.Clamp(e.Delta.Y > 0 ? _zoom * ZoomStep : _zoom / ZoomStep, Math.Min(fit / 2, 1), MaxZoom);
        _offset = new Point(sceneAnchor.X - anchor.X / _zoom, sceneAnchor.Y - anchor.Y / _zoom);
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
        var scenePosition = ToScene(position);
        var point = new ScenePoint((int)Math.Floor(scenePosition.X), (int)Math.Floor(scenePosition.Y), Time, _zoom);
        if (command?.CanExecute(point) == true)
        {
            command.Execute(point);
        }
    }

    private void DrawOverlay(DrawingContext context, MapScene scene)
    {
        var overlay = Overlay;
        if (overlay is null)
        {
            return;
        }

        var painter = new OverlayPainter(context, point => new Point((point.X - scene.Left - _offset.X) * _zoom, (point.Y - scene.Top - _offset.Y) * _zoom),
            new Rect(Bounds.Size), _zoom);
        if (ShowsRoute)
        {
            painter.DrawRoute(overlay);
        }

        if (ShowsScriptMarkers)
        {
            painter.DrawMarkers(overlay.Logic.Markers);
        }

        if (ShowsHelpers)
        {
            painter.DrawHelpers(overlay.Helpers);
        }
    }

    private void UpdateTimer()
    {
        if (Animates && Scene?.IsAnimated == true && _attached)
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
        Pump();
    }

    private async void Pump()
    {
        var scene = Scene;
        if (_busy || scene is null || scene.IsEmpty)
        {
            return;
        }

        var job = GetNextJob(scene);
        if (job is null)
        {
            return;
        }

        _busy = true;
        var watch = Stopwatch.StartNew();
        try
        {
            await Task.Run(job.Run);
            Debug.WriteLine($"{job} took {watch.ElapsedMilliseconds} ms");
            if (job.Scene == Scene)
            {
                job.Apply();
                EvictTiles(scene);
            }
        }
        finally
        {
            _busy = false;
        }

        InvalidateVisual();
        Pump();
    }

    private Job? GetNextJob(MapScene scene)
    {
        int level = GetLevel();
        var visible = GetVisibleTiles(scene, level).ToList();
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        
        var missing = visible
            .Where(key => !_tiles.ContainsKey(key))
            .OrderBy(key => GetDistance(ToView(GetTileArea(scene, key), GetScale(level)).Center, center))
            .Cast<TileKey?>()
            .FirstOrDefault();
        
        if (missing.HasValue)
        {
            var key = missing.Value;
            var tile = new Tile(GetTileArea(scene, key), GetScale(level)) { LastDrawn = _frameNumber };
            _tiles[key] = tile;
            return new TileJob(scene, tile, Time);
        }

        if (!_tickDue || Time is null || level > MaxAnimatedLevel)
        {
            return null;
        }

        _tickDue = false;
        var patched = visible.Select(visibleKey => _tiles[visibleKey]).Where(tile => tile.Bitmap is not null && tile.Time != Time.Value).ToArray();
        return patched.Length > 0 ? new PatchJob(scene, patched, Time.Value) : null;
    }

    private void EvictTiles(MapScene scene)
    {
        if (_tiles.Count <= MaxTiles)
        {
            return;
        }

        var visible = GetVisibleTiles(scene, GetLevel()).ToHashSet();
        foreach (var key in _tiles.Where(pair => pair.Value.Bitmap is not null && !visible.Contains(pair.Key)).OrderBy(pair => pair.Value.LastDrawn)
                     .Take(_tiles.Count - MaxTiles).Select(pair => pair.Key).ToList())
        {
            _tiles[key].Bitmap?.Dispose();
            _tiles.Remove(key);
        }
    }

    private void ClearTiles()
    {
        foreach (var tile in _tiles.Values)
        {
            tile.Bitmap?.Dispose();
        }

        _tiles.Clear();
    }

    private IEnumerable<TileKey> GetVisibleTiles(MapScene scene, int level)
    {
        double scale = GetScale(level);
        var topLeft = ToScene(default);
        var bottomRight = ToScene(new Point(Bounds.Width, Bounds.Height));
        int columns = (GetOutputSize(scene.Width, scale) + TileSize - 1) / TileSize;
        int rows = (GetOutputSize(scene.Height, scale) + TileSize - 1) / TileSize;
        int firstColumn = Math.Max(0, (int)Math.Floor(topLeft.X * scale / TileSize));
        int lastColumn = Math.Min(columns - 1, (int)Math.Floor(bottomRight.X * scale / TileSize));
        int firstRow = Math.Max(0, (int)Math.Floor(topLeft.Y * scale / TileSize));
        int lastRow = Math.Min(rows - 1, (int)Math.Floor(bottomRight.Y * scale / TileSize));
        for (int row = firstRow; row <= lastRow; row++)
        {
            for (int column = firstColumn; column <= lastColumn; column++)
            {
                yield return new TileKey(level, column, row);
            }
        }
    }

    private static Rectangle GetTileArea(MapScene scene, TileKey key)
    {
        double scale = GetScale(key.Level);
        int left = key.Column * TileSize;
        int top = key.Row * TileSize;
        return new Rectangle(left, top, Math.Min(TileSize, GetOutputSize(scene.Width, scale) - left), Math.Min(TileSize, GetOutputSize(scene.Height, scale) - top));
    }

    private static int GetOutputSize(int size, double scale) => Math.Max(1, (int)Math.Ceiling(size * scale));

    // Level n draws the scene at a scale of 1 / 2^n, the smallest one that is still at least the zoom
    private int GetLevel() => Scene is null ? 0 : Math.Clamp((int)Math.Floor(-Math.Log2(_zoom)), 0, GetMaxLevel(Scene));

    private static int GetMaxLevel(MapScene scene) => Math.Max(0, (int)Math.Ceiling(Math.Log2(Math.Max(scene.Width, scene.Height) / (double)TileSize)));

    private static double GetScale(int level) => 1.0 / (1 << level);

    private Point ToScene(Point view) => new(_offset.X + view.X / _zoom, _offset.Y + view.Y / _zoom);

    // Snap edges to whole pixels to keep blended seams out from between tiles
    private Rect ToView(Rectangle area, double scale)
    {
        double left = Math.Round((area.Left / scale - _offset.X) * _zoom);
        double top = Math.Round((area.Top / scale - _offset.Y) * _zoom);
        double right = Math.Round((area.Right / scale - _offset.X) * _zoom);
        double bottom = Math.Round((area.Bottom / scale - _offset.Y) * _zoom);
        return new Rect(left, top, right - left, bottom - top);
    }

    private static double GetDistance(Point first, Point second) => Math.Abs(first.X - second.X) + Math.Abs(first.Y - second.Y);

    private readonly record struct TileKey(int Level, int Column, int Row);

    private sealed class Tile
    {
        public Tile(Rectangle area, double scale)
        {
            Area = area;
            Scale = scale;
            Pixels = new Bgra32[area.Width * area.Height];
        }

        public Rectangle Area { get; }

        public double Scale { get; }

        public Bgra32[] Pixels { get; }

        public WriteableBitmap? Bitmap { get; set; }

        public int? Time { get; set; }

        public long LastDrawn { get; set; }

        public unsafe void Upload()
        {
            Bitmap ??= new WriteableBitmap(new PixelSize(Area.Width, Area.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using var buffer = Bitmap.Lock();
            for (int row = 0; row < Area.Height; row++)
            {
                Pixels.AsSpan(row * Area.Width, Area.Width).CopyTo(new Span<Bgra32>((byte*)buffer.Address + row * buffer.RowBytes, Area.Width));
            }
        }
    }

    private abstract class Job
    {
        protected Job(MapScene scene)
        {
            Scene = scene;
        }

        public MapScene Scene { get; }

        public abstract void Run();

        public abstract void Apply();
    }

    private sealed class TileJob : Job
    {
        private readonly Tile _tile;
        private readonly int? _time;

        public TileJob(MapScene scene, Tile tile, int? time) : base(scene)
        {
            _tile = tile;
            _time = time;
        }

        public override void Run() => Scene.Render(_tile.Pixels, _tile.Area.Width, _tile.Area, _tile.Scale, _time);

        public override string ToString() => $"Tile {_tile.Area} at scale {_tile.Scale}";

        public override void Apply()
        {
            _tile.Time = _time;
            _tile.Upload();
        }
    }

    private sealed class PatchJob : Job
    {
        private readonly Tile[] _tiles;
        private readonly int _time;
        private readonly List<Tile> _changed = [];

        public PatchJob(MapScene scene, Tile[] tiles, int time) : base(scene)
        {
            _tiles = tiles;
            _time = time;
        }

        public override void Run()
        {
            var changes = new List<Rectangle>();
            foreach (var tile in _tiles)
            {
                changes.Clear();
                var sceneArea = Rectangle.FromLTRB((int)(tile.Area.Left / tile.Scale), (int)(tile.Area.Top / tile.Scale), (int)Math.Ceiling(tile.Area.Right / tile.Scale),
                    (int)Math.Ceiling(tile.Area.Bottom / tile.Scale));
                Scene.GetChangedAreas(tile.Time!.Value, _time, sceneArea, changes);
                if (changes.Count > 0)
                {
                    Patch(tile, changes);
                    _changed.Add(tile);
                }

                tile.Time = _time;
            }
        }

        public override string ToString() => $"Patch of {_changed.Count} of {_tiles.Length} tiles";

        public override void Apply()
        {
            foreach (var tile in _changed)
            {
                tile.Upload();
            }
        }

        private void Patch(Tile tile, List<Rectangle> changes)
        {
            var areas = new List<Rectangle>();
            long changedPixels = 0;
            foreach (var change in changes)
            {
                var area = Rectangle.Intersect(tile.Area, Rectangle.FromLTRB((int)Math.Floor(change.Left * tile.Scale), (int)Math.Floor(change.Top * tile.Scale),
                    (int)Math.Ceiling(change.Right * tile.Scale), (int)Math.Ceiling(change.Bottom * tile.Scale)));
                if (!area.IsEmpty)
                {
                    areas.Add(area);
                    changedPixels += (long)area.Width * area.Height;
                }
            }

            if (changedPixels > PatchShare * tile.Area.Width * tile.Area.Height)
            {
                areas = [tile.Area];
            }

            MergeOverlapping(areas);

            foreach (var area in areas)
            {
                int start = (area.Top - tile.Area.Top) * tile.Area.Width + area.Left - tile.Area.Left;
                Scene.Render(tile.Pixels.AsMemory(start), tile.Area.Width, area, tile.Scale, _time);
            }
        }

        private static void MergeOverlapping(List<Rectangle> areas)
        {
            for (int first = 0; first < areas.Count; first++)
            {
                for (int second = first + 1; second < areas.Count; second++)
                {
                    if (areas[first].IntersectsWith(areas[second]))
                    {
                        areas[first] = Rectangle.Union(areas[first], areas[second]);
                        areas.RemoveAt(second);
                        second = first;
                    }
                }
            }
        }
    }
}
