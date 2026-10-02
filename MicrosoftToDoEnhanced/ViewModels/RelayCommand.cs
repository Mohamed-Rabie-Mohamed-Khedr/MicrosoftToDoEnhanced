using System.Windows.Input;

namespace MicrosoftToDoEnhanced.ViewModels;

public class RelayCommand : RelayCommand<object?>
{
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : base(Disregarding(execute), canExecute is null ? null : _ => canExecute())
    {
    }

    private static Action<object?> Disregarding(Action execute)
    {
        if (execute is null)
            throw new ArgumentNullException(nameof(execute));
        return _ => execute();
    }
}

public class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<T?, bool>? _canExecute;

    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) =>
        _canExecute?.Invoke(parameter is T value ? value : default) ?? true;

    public void Execute(object? parameter) =>
        _execute(parameter is T value ? value : default);
}

public class AsyncRelayCommand : AsyncRelayCommand<object?>
{
    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null, Action<Exception>? onError = null)
        : base(Disregarding(execute), canExecute is null ? null : _ => canExecute(), onError)
    {
    }

    private static Func<object?, Task> Disregarding(Func<Task> execute)
    {
        if (execute is null)
            throw new ArgumentNullException(nameof(execute));
        return _ => execute();
    }
}

public class AsyncRelayCommand<T> : ICommand
{
    private readonly Func<T?, Task> _execute;
    private readonly Func<T?, bool>? _canExecute;
    private readonly Action<Exception>? _onError;
    private bool _isRunning;

    public AsyncRelayCommand(Func<T?, Task> execute, Func<T?, bool>? canExecute = null, Action<Exception>? onError = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _onError = onError;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) =>
        !_isRunning && (_canExecute?.Invoke(parameter is T value ? value : default) ?? true);

    public async void Execute(object? parameter)
    {
        if (_isRunning)
            return;

        _isRunning = true;
        RaiseCanExecuteChanged();
        try
        {
            await _execute(parameter is T value ? value : default);
        }
        catch (Exception exception)
        {
            _onError?.Invoke(exception);
        }
        finally
        {
            _isRunning = false;
            RaiseCanExecuteChanged();
        }
    }

    private void RaiseCanExecuteChanged() =>
        CommandManager.InvalidateRequerySuggested();
}