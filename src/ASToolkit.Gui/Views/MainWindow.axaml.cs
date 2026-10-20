using ASToolkit.Gui.ViewModels;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace ASToolkit.Gui.Views;

public partial class MainWindow : Window
{
    private IDisposable? _selectFilesInteractionDisposable;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        // Dispose any old handler
        _selectFilesInteractionDisposable?.Dispose();

        if (DataContext is MainViewModel vm)
        {
            // register the interaction handler
            _selectFilesInteractionDisposable =
                vm.SelectFilesInteraction.RegisterHandler(InteractionHandler);
        }

        base.OnDataContextChanged(e);
    }

    private async Task<IReadOnlyList<IStorageFile>> InteractionHandler(FilePickerOpenOptions options)
    {
        // Get a reference to our TopLevel (in our case the parent Window)
        var topLevel = GetTopLevel(this);

        // Try to get the files
        return await topLevel!.StorageProvider.OpenFilePickerAsync(options);
    }
}
