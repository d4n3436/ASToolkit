using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ASToolkit.Gui.ViewModels;

namespace ASToolkit.Gui;

public class ViewLocator : IDataTemplate
{
    public Control Build(object? data)
    {
        return data switch
        {
            MapViewerViewModel => new Views.MapViewerView(),
            ReplayViewModel => new Views.ReplayView(),
            ResInspectorViewModel => new Views.ResInspectorView(),
            _ => new TextBlock { Text = $"No view for {data!.GetType().Name}" }
        };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
