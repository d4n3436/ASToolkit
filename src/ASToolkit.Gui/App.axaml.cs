using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ASToolkit.Gui.ViewModels;
using ASToolkit.Gui.Views;

namespace ASToolkit.Gui;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainViewModel();
            if (desktop.Args?.Length > 0 && Directory.Exists(desktop.Args[0]))
            {
                viewModel.Viewer.OpenGame(desktop.Args[0], desktop.Args.Length > 1 ? desktop.Args[1] : null);
                viewModel.SelectedTab = 1;
            }

            desktop.MainWindow = new MainWindow { DataContext = viewModel };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
