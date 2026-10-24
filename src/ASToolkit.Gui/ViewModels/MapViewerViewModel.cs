using System.Collections.ObjectModel;
using System.Numerics;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Rendering;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Scripting;
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

    private readonly Dictionary<string, MapOverlay> _overlays = new(StringComparer.OrdinalIgnoreCase);
    private ObjectTable? _objects;
    private QuestBook _quests = QuestBook.Empty;
    private MapRenderer? _renderer;
    private string _summary = "";
    private int _loadVersion;

    public Interaction<FolderPickerOpenOptions, IReadOnlyList<IStorageFolder>> SelectFolderInteraction { get; } = new();

    public ObservableCollection<string> Maps { get; } = [];

    public SpriteViewModel Sprite { get; } = new();

    [ObservableProperty]
    public partial string? GameFolder { get; set; }

    [ObservableProperty]
    public partial string? SelectedMap { get; set; }

    [ObservableProperty]
    public partial MapScene? Scene { get; set; }

    [ObservableProperty]
    public partial MapOverlay? Overlay { get; set; }

    [ObservableProperty]
    public partial bool IsAlienShooter2 { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "Open a game folder to view its maps";

    [ObservableProperty]
    public partial bool Animates { get; set; } = true;

    [ObservableProperty]
    public partial bool MatchGameLighting { get; set; }

    [ObservableProperty]
    public partial bool OmitAmbientTint { get; set; }

    [ObservableProperty]
    public partial bool OmitCovers { get; set; }

    [ObservableProperty]
    public partial bool ShowsHiddenObjects { get; set; }

    [ObservableProperty]
    public partial bool ShowsFemalePlayer { get; set; }

    [ObservableProperty]
    public partial bool OmitRemovableGroups { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowsHelpers { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowsScriptMarkers { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowsRoute { get; set; } = true;

    [RelayCommand]
    public async Task SelectGameFolderAsync()
    {
        var folders = await SelectFolderInteraction.HandleAsync(new FolderPickerOpenOptions { Title = "Open the AS1/AS2 game folder" });
        string? path = folders.Count >= 1 ? folders[0].TryGetLocalPath() : null;
        if (path is not null)
        {
            OpenGame(path);
        }
    }

    /// <summary>
    /// Lists the maps of a game folder and opens the given one, or the first one.
    /// </summary>
    public void OpenGame(string folder, string? map = null)
    {
        string? objectsPath = GamePath.Resolve(folder, ObjectsFile);
        if (objectsPath is null)
        {
            Status = $"{folder} has no {ObjectsFile}";
            return;
        }

        _objects = ObjectTable.Load(objectsPath);
        _quests = QuestBook.Load(folder);
        _overlays.Clear();
        _renderer = null;
        GameFolder = folder;
        IsAlienShooter2 = _objects.IsAlienShooter2;
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
            Scene = null;
            Status = $"{folder} has no maps";
        }
    }

    [RelayCommand]
    public void Hover(ScenePoint point)
    {
        var scene = Scene;
        if (scene is null || _objects is null)
        {
            return;
        }

        string position = $"screen ({scene.Left + point.X}, {scene.Top + point.Y})";
        string? overlay = DescribeOverlay(new Vector2(scene.Left + point.X, scene.Top + point.Y), point.Zoom);
        if (overlay is not null)
        {
            Status = $"{position}    {overlay}";
            return;
        }

        var found = scene.FindObject(point.X, point.Y, point.Time);
        Status = found is null ? $"{position}    {_summary}"
            : $"{position}    object {(int)found.Id} {_objects.GetName(found.Id)} [{_objects.GetPath(found.Id)}]    " +
              $"position ({found.Position.X:0}, {found.Position.Y:0}, {found.Position.Z:0})    facing {found.Facing}";
    }

    private string? DescribeOverlay(Vector2 position, double zoom)
    {
        var overlay = Overlay;
        if (overlay is null || _objects is null)
        {
            return null;
        }

        float reach = (float)(HoverDistance / zoom);
        if (ShowsHelpers)
        {
            var helper = overlay.Helpers.FirstOrDefault(helper =>
                Vector2.Distance(ToScreen(helper.Sprite.Position), position) <= reach);
            if (helper is not null)
            {
                string name = _objects.TryFind(helper.Sprite.Id, out _) ? _objects.GetName(helper.Sprite.Id) : "unknown object";
                return $"{helper.Kind.ToString().ToLowerInvariant()} helper {(int)helper.Sprite.Id} {name}    army {helper.Sprite.Army}";
            }
        }

        if (ShowsRoute)
        {
            var step = (overlay.Walk?.Steps ?? overlay.Logic.Progression).Select((step, index) => (Step: step, Index: index))
                .FirstOrDefault(entry => Vector2.Distance(ToScreen(entry.Step.Position), position) <= reach);
            if (step.Step is not null)
            {
                return $"step {step.Index + 1}: {step.Step.Label}";
            }
        }

        if (!ShowsScriptMarkers)
        {
            return null;
        }

        var marker = overlay.Logic.Markers
            .Where(marker => Vector2.Distance(ToScreen(marker.Position), position) <= Math.Max(reach, marker.Radius) || Vector2.Distance(ToScreen(marker.Source), position) <= reach)
            .OrderByDescending(marker => marker.Kind)
            .ThenBy(marker => marker.Radius)
            .FirstOrDefault();
        return marker is null ? null : $"{marker.Kind.ToString().ToLowerInvariant()}: {marker.Label}";
    }

    [RelayCommand]
    public void Select(ScenePoint point)
    {
        var found = Scene?.FindObject(point.X, point.Y, point.Time);
        if (found is not null && _renderer is not null && _objects is not null)
        {
            Sprite.Show(_renderer, _objects, found);
        }
    }

    partial void OnSelectedMapChanged(string? value) => LoadScene();

    partial void OnMatchGameLightingChanged(bool value) => ResetRenderer();

    partial void OnOmitAmbientTintChanged(bool value) => ResetRenderer();

    partial void OnOmitCoversChanged(bool value) => ResetRenderer();

    partial void OnShowsHiddenObjectsChanged(bool value) => ResetRenderer();

    partial void OnShowsFemalePlayerChanged(bool value) => ResetRenderer();

    partial void OnOmitRemovableGroupsChanged(bool value) => ResetRenderer();

    private void ResetRenderer()
    {
        _renderer = null;
        LoadScene();
    }

    private async void LoadScene()
    {
        string? game = GameFolder;
        string? map = SelectedMap;
        var objects = _objects;
        string? mapsPath = game is null ? null : GamePath.Resolve(game, MapsFolder);
        if (game is null || map is null || objects is null || mapsPath is null)
        {
            return;
        }

        int version = ++_loadVersion;
        var renderer = _renderer ??= new MapRenderer(game, objects)
        {
            MatchGameLighting = MatchGameLighting, OmitAmbientTint = OmitAmbientTint, OmitCovers = OmitCovers, OmitHiddenObjects = !ShowsHiddenObjects,
            ShowsFemalePlayer = ShowsFemalePlayer, OmitRemovableGroups = OmitRemovableGroups
        };
        IsLoading = true;
        Status = $"Loading {map}";
        try
        {
            string path = Path.Combine(mapsPath, map);
            (var resource, var scene) = await Task.Run(() =>
            {
                var loaded = MapResourceBuilder.Load(path);
                return (loaded, renderer.CreateScene(loaded.Sprites, loaded.Graphics));
            });
            if (version != _loadVersion)
            {
                return;
            }

            _summary = scene.IsEmpty ? "This map has no sprites"
                : $"{resource.Sprites.Count} sprites, {scene.ObjectCount} objects drawn, {scene.AnimatedCount} animated, {scene.Undrawn} sprite frames not decoded";
            if (!_overlays.TryGetValue(map, out var overlay))
            {
                Overlay = null;
            }

            Scene = scene;
            Status = _summary;
            IsLoading = false;
            if (overlay is null)
            {
                var quests = _quests;
                overlay = await Task.Run(() => CreateOverlay(game, map, path, resource, objects, quests, renderer));
                if (version != _loadVersion)
                {
                    return;
                }

                _overlays[map] = overlay;
            }

            Overlay = overlay;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            if (version == _loadVersion)
            {
                Scene = null;
                Overlay = null;
                Status = $"{map} cannot be opened: {exception.Message}";
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

    private static Vector2 ToScreen(Vector3 point) => new(point.X, point.Y - point.Z);

    private static MapOverlay CreateOverlay(string game, string map, string mapPath, MapResourceBuilder resource, ObjectTable objects, QuestBook quests,
        MapRenderer renderer)
    {
        string? scriptPath = LevelLogicReader.FindLevelScript(mapPath);
        var logic = LevelLogicReader.Read(mapPath, resource, objects, quests);
        var route = RouteAnalyzer.Analyze(map, resource, scriptPath is null ? new LevelScriptSummary() : LevelScriptReader.Read(scriptPath), objects);
        return new MapOverlay
        {
            Logic = logic, Helpers = renderer.GetHelpers(resource.Sprites), Route = route, TourRuns = route?.GetTourRuns() ?? [],
            Walk = objects.IsAlienShooter2 ? WalkedRouteBuilder.Build(game, resource, objects, logic) : null
        };
    }
}
