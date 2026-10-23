using ASToolkit.Gui.ViewModels;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace ASToolkit.Gui.Views;

public partial class ResInspectorView : UserControl
{
    private IDisposable? _selectFilesInteractionDisposable;

    public ResInspectorView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        _selectFilesInteractionDisposable?.Dispose();

        if (DataContext is ResInspectorViewModel vm)
        {
            _selectFilesInteractionDisposable =
                vm.SelectFilesInteraction.RegisterHandler(InteractionHandler);
        }

        base.OnDataContextChanged(e);
    }

    private async Task<IReadOnlyList<IStorageFile>> InteractionHandler(FilePickerOpenOptions options)
    {
        var topLevel = TopLevel.GetTopLevel(this);

        return await topLevel!.StorageProvider.OpenFilePickerAsync(options);
    }
}
