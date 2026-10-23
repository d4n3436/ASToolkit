using CommunityToolkit.Mvvm.ComponentModel;

namespace ASToolkit.Gui.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public ResInspectorViewModel Inspector { get; } = new();

    public MapViewerViewModel Viewer { get; } = new();

    [ObservableProperty]
    public partial int SelectedTab { get; set; }
}
