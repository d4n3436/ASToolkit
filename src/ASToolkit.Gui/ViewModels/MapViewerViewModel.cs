using System.Collections.ObjectModel;
using System.Numerics;
using ASToolkit.Core;
using ASToolkit.Engine;
using ASToolkit.Formats;
using ASToolkit.Analysis.Routes;
using ASToolkit.Analysis.Scripting;
using ASToolkit.Bot;
using ASToolkit.Gui.Core;
using ASToolkit.Gui.Models;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ASToolkit.Gui.ViewModels;

/// <summary>
/// Represents the map viewer of a game folder.
/// </summary>
public partial class MapViewerViewModel : ViewModelBase
{
    private const string ObjectsFile = "objects.res";
    private const string MapsFolder = "Maps";
    private const double HoverDistance = 6;
    private const int LayerCount = 15;

    private readonly Dictionary<string, MapOverlay> _overlays = new(StringComparer.OrdinalIgnoreCase);
    private QuestBook _quests = QuestBook.Empty;
    private string _summary = "";
    private int _loadVersion;

    public Interaction<FolderPickerOpenOptions, IReadOnlyList<IStorageFolder>> SelectFolderInteraction { get; } = new();

    public ObservableCollection<string> Maps { get; } = [];

    public SpriteViewModel Sprite { get; } = new();

    /// <summary>
    /// Gets the object table of the open game folder, or null when no game folder is open.
    /// </summary>
    public ObjectTable? Objects { get; private set; }

    [ObservableProperty]
    public partial string? GameFolder { get; set; }

    [ObservableProperty]
    public partial string? SelectedMap { get; set; }

    [ObservableProperty]
    public partial Map? Map { get; set; }

    [ObservableProperty]
    public partial MapOverlay? Overlay { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "Open a game folder to view its maps";

    [ObservableProperty]
    public partial bool Animate { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowHelpers { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowScriptMarkers { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowRoute { get; set; } = true;

    [RelayCommand]
    public async Task SelectGameFolderAsync()
    {
        var folders = await SelectFolderInteraction.HandleAsync(new FolderPickerOpenOptions { Title = "Open the Alien Shooter game folder" });
        string? path = folders.Count >= 1 ? folders[0].TryGetLocalPath() : null;
        if (path is not null)
        {
            OpenGame(path);
        }
    }

    /// <summary>
    /// Lists the maps of a game folder and opens the given one, or the first one.
    /// </summary>
    /// <param name="folder">The game folder.</param>
    /// <param name="map">The name of the map to open, or null to open the first map.</param>
    public void OpenGame(string folder, string? map = null)
    {
        string? objectsPath = GamePath.Resolve(folder, ObjectsFile);
        if (objectsPath is null)
        {
            Status = $"{folder} has no {ObjectsFile}";
            return;
        }

        var objects = ObjectTable.Load(objectsPath);
        if (objects.IsAlienShooter2)
        {
            Status = $"{folder} is an Alien Shooter 2 folder. Only Alien Shooter maps can be viewed.";
            return;
        }

        Objects = objects;
        _quests = QuestBook.Load(folder);
        _overlays.Clear();
        GameFolder = folder;
        string? mapsPath = GamePath.Resolve(folder, MapsFolder);
        Maps.Clear();
        if (mapsPath is not null)
        {
            foreach (string path in Directory.EnumerateFiles(mapsPath, "*.map", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
            {
                Maps.Add(Path.GetRelativePath(mapsPath, path));
            }
        }

        SelectedMap = Maps.FirstOrDefault(name => string.Equals(name, map, StringComparison.OrdinalIgnoreCase)) ?? Maps.FirstOrDefault();
        if (SelectedMap is null)
        {
            Map = null;
            Status = $"{folder} has no maps";
        }
    }

    [RelayCommand]
    public void Hover(MapPoint point)
    {
        if (Map is null || Objects is null)
        {
            return;
        }

        string position = $"map ({point.Position.X}, {point.Position.Y})";
        string? overlay = DescribeOverlay(new Vector2(point.Position.X, point.Position.Y), point.Zoom);
        if (overlay is not null)
        {
            Status = $"{position}    {overlay}";
            return;
        }

        var found = FindSprite(Map, point.Position);
        Status = found is null ? $"{position}    {_summary}"
            : $"{position}    object {(int)found.Definition.Id} {Objects.GetName(found.Definition.Id)} [{Objects.GetPath(found.Definition.Id)}]    " +
              $"position ({found.Position.X:0}, {found.Position.Y:0}, {found.Position.Z:0})    direction {found.Direction}";
    }

    [RelayCommand]
    public void Select(MapPoint point)
    {
        var found = Map is null ? null : FindSprite(Map, point.Position);
        if (Map is not null && found is not null && Objects is not null)
        {
            Sprite.Show(Map, Objects, found);
        }
    }

    partial void OnSelectedMapChanged(string? value) => LoadMap();

    private string? DescribeOverlay(Vector2 position, double zoom)
    {
        var overlay = Overlay;
        if (overlay is null || Objects is null)
        {
            return null;
        }

        float reach = (float)(HoverDistance / zoom);
        if (ShowHelpers)
        {
            var helper = overlay.Helpers.FirstOrDefault(helper => Vector2.Distance(helper.Position.ToDrawPosition(), position) <= reach);
            if (helper is not null)
            {
                string name = Objects.TryFind(helper.Id, out _) ? Objects.GetName(helper.Id) : "unknown object";
                return $"{helper.Kind.ToString().ToLowerInvariant()} helper {(int)helper.Id} {name}    army {helper.Army}";
            }
        }

        if (ShowRoute)
        {
            var step = overlay.Logic.Progression.Select((step, index) => (Step: step, Index: index))
                .FirstOrDefault(entry => Vector2.Distance(entry.Step.Position.ToDrawPosition(), position) <= reach);
            if (step.Step is not null)
            {
                return $"step {step.Index + 1}: {step.Step.Label}";
            }
        }

        if (!ShowScriptMarkers)
        {
            return null;
        }

        var marker = overlay.Logic.Markers
            .Where(marker => Vector2.Distance(marker.Position.ToDrawPosition(), position) <= Math.Max(reach, marker.Radius) || Vector2.Distance(marker.Source.ToDrawPosition(), position) <= reach)
            .OrderByDescending(marker => marker.Kind)
            .ThenBy(marker => marker.Radius)
            .FirstOrDefault();
        return marker is null ? null : $"{marker.Kind.ToString().ToLowerInvariant()}: {marker.Label}";
    }

    private async void LoadMap()
    {
        string? game = GameFolder;
        string? name = SelectedMap;
        var objects = Objects;
        string? mapsPath = game is null ? null : GamePath.Resolve(game, MapsFolder);
        if (game is null || name is null || objects is null || mapsPath is null)
        {
            return;
        }

        int version = ++_loadVersion;
        IsLoading = true;
        Status = $"Loading {name}";
        try
        {
            string path = Path.Combine(mapsPath, name);
            var (resource, map) = await Task.Run(() => (MapResourceBuilder.Load(path), Playthrough.LoadMap(game, new SpriteCatalog(game, objects), path)));
            if (version != _loadVersion)
            {
                return;
            }

            _summary = $"{resource.Sprites.Count} sprites placed";
            if (!_overlays.TryGetValue(name, out var overlay))
            {
                Overlay = null;
            }

            Map = map;
            Status = _summary;
            IsLoading = false;
            if (overlay is null)
            {
                var quests = _quests;
                overlay = await Task.Run(() => CreateOverlay(name, path, resource, objects, quests, map));
                if (version != _loadVersion)
                {
                    return;
                }

                _overlays[name] = overlay;
            }

            Overlay = overlay;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            if (version == _loadVersion)
            {
                Map = null;
                Overlay = null;
                Status = $"{name} cannot be opened: {ex.Message}";
            }
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    private static Sprite? FindSprite(Map map, System.Drawing.Point point)
    {
        if (!Monitor.TryEnter(map))
            return null;

        try
        {
            for (int layer = LayerCount - 1; layer >= 0; layer--)
            {
                foreach (var sprite in map.GetLayer(layer))
                {
                    if (sprite is null || IsHidden(sprite) || !IsPlaced(map, sprite))
                        continue;

                    var center = sprite.Position.ToDrawPosition();
                    if (Math.Abs(point.X - center.X) <= sprite.Definition.FrameSize.Center.X && Math.Abs(point.Y - center.Y) <= sprite.Definition.FrameSize.Center.Y)
                        return sprite;
                }
            }
        }
        finally
        {
            Monitor.Exit(map);
        }

        return null;
    }

    private static bool IsPlaced(Map map, Sprite sprite) => sprite != map.CursorSprite && !map.MenuSprites.Contains(sprite);

    private static bool IsHidden(Sprite sprite) => sprite.IsInvisible || sprite.Definition.IsHidden;

    private static IReadOnlyList<HelperMarker> GetHelpers(Map map)
    {
        var helpers = new List<HelperMarker>();
        lock (map)
        {
            for (int layer = 0; layer < LayerCount; layer++)
            {
                foreach (var sprite in map.GetLayer(layer))
                {
                    if (sprite is not null && IsPlaced(map, sprite) && IsHidden(sprite))
                    {
                        helpers.Add(new HelperMarker(sprite.Definition.Id, sprite.Position, sprite.Army, GetHelperKind(sprite.Definition.Object.SpriteClass)));
                    }
                }
            }
        }

        return helpers;
    }

    private static HelperKind GetHelperKind(SpriteClass spriteClass) => spriteClass switch
    {
        SpriteClass.Building => HelperKind.Spawner,
        SpriteClass.Sprite => HelperKind.Trigger,
        SpriteClass.BuiltTerrain => HelperKind.Blocker,
        _ => HelperKind.Other
    };

    private static MapOverlay CreateOverlay(string name, string mapPath, MapResourceBuilder resource, ObjectTable objects, QuestBook quests, Map map)
    {
        string? scriptPath = LevelLogicReader.FindLevelScript(mapPath);
        var logic = LevelLogicReader.Read(mapPath, resource, objects, quests);
        var route = RouteAnalyzer.Analyze(name, resource, scriptPath is null ? new LevelScriptSummary() : LevelScriptReader.Read(scriptPath, objects.ScriptVersion), objects);
        return new MapOverlay { Logic = logic, Helpers = GetHelpers(map), Route = route, TourRuns = route?.GetTourRuns() ?? [] };
    }
}
