using System.Collections.ObjectModel;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Rendering;
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

    private ObjectTable? _objects;
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
        var found = scene.FindObject(point.X, point.Y, point.Time);
        Status = found is null ? $"{position}    {_summary}"
            : $"{position}    object {(int)found.Id} {_objects.GetName(found.Id)} [{_objects.GetPath(found.Id)}]    " +
              $"position ({found.X:0}, {found.Y:0}, {found.Z:0})    facing {found.Facing}";
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
            (int spriteCount, var scene) = await Task.Run(() =>
            {
                var resource = MapResourceBuilder.Load(Path.Combine(mapsPath, map));
                return (resource.Sprites.Count, renderer.CreateScene(resource.Sprites, resource.Graphics));
            });
            if (version != _loadVersion)
            {
                return;
            }

            _summary = scene.IsEmpty ? "This map has no sprites"
                : $"{spriteCount} sprites, {scene.ObjectCount} objects drawn, {scene.AnimatedCount} animated, {scene.Undrawn} sprite frames not decoded";
            Scene = scene;
            Status = _summary;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            if (version == _loadVersion)
            {
                Scene = null;
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
}
