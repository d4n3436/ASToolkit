using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ASToolkit.Gui.ViewModels;
using StaticViewLocator;

namespace ASToolkit.Gui;

/// <summary>
/// Given a view model, returns the corresponding view if possible.
/// </summary>
[StaticViewLocator]
public partial class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is null)
        {
            return null;
        }

        var type = data.GetType();
        var func = TryGetFactory(type) ?? TryGetFactoryFromInterfaces(type);

        if (func is not null)
        {
            return func.Invoke();
        }

        string? missingView = TryGetMissingView(type) ?? TryGetMissingViewFromInterfaces(type);
        
        return missingView is not null
            ? new TextBlock { Text = missingView }
            : throw new Exception($"Unable to create view for type: {type}");
    }

    public bool Match(object? data) => data is ViewModelBase;
}
