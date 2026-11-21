using System.Windows.Input;

namespace ASToolkit.Gui.Core;

/// <summary>
/// Provides a simple implementation of the Interaction pattern.
/// </summary>
/// <typeparam name="TInput">The type of the input of the interaction.</typeparam>
/// <typeparam name="TOutput">The type of the result of the interaction.</typeparam>
public sealed class Interaction<TInput, TOutput> : IDisposable, ICommand
{
    private Func<TInput, Task<TOutput>>? _handler;

    /// <summary>
    /// Performs the requested interaction asynchronously.
    /// </summary>
    /// <param name="input">The input parameter.</param>
    /// <returns>The result that the view provides.</returns>
    /// <exception cref="InvalidOperationException">No handler has been registered.</exception>
    public Task<TOutput> HandleAsync(TInput input)
    {
        if (_handler is null)
        {
            throw new InvalidOperationException("No handler has been registered.");
        }

        return _handler(input);
    }

    /// <summary>
    /// Registers a handler for the interaction.
    /// </summary>
    /// <param name="handler">The handler to register.</param>
    /// <returns>A disposable object that removes the handler when it is disposed.</returns>
    /// <exception cref="InvalidOperationException">A handler has already been registered.</exception>
    public IDisposable RegisterHandler(Func<TInput, Task<TOutput>> handler)
    {
        if (_handler is not null)
        {
            throw new InvalidOperationException("A handler has already been registered.");
        }

        _handler = handler;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        return this;
    }

    public void Dispose()
    {
        _handler = null;
    }

    public bool CanExecute(object? parameter) => _handler is not null;

    public void Execute(object? parameter) => HandleAsync((TInput?)parameter!);

    public event EventHandler? CanExecuteChanged;
}
