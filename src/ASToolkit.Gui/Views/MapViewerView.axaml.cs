using ASToolkit.Gui.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace ASToolkit.Gui.Views;

public partial class MapViewerView : UserControl
{
    private IDisposable? _selectFolderInteractionDisposable;

    public MapViewerView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        _selectFolderInteractionDisposable?.Dispose();
        if (DataContext is MapViewerViewModel vm)
        {
            _selectFolderInteractionDisposable = vm.SelectFolderInteraction.RegisterHandler(SelectFolderAsync);
        }

        base.OnDataContextChanged(e);
    }

    private async Task<IReadOnlyList<IStorageFolder>> SelectFolderAsync(FolderPickerOpenOptions options) =>
        await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(options);

    private void OnFitClick(object? sender, RoutedEventArgs e) => Map.Fit();
}
