using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Core.Repositories;

namespace MicrosoftToDoEnhanced.ViewModels;

public static class TaskExtensions
{
    public static async void SafeFireAndForget(this Task task, Action<Exception>? onError = null)
    {
        try
        {
            await task;
        }
        catch (Exception exception)
        {
            onError?.Invoke(exception);
        }
    }
}

public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected static string DescribeError(Exception exception) =>
        string.IsNullOrWhiteSpace(exception.Message)
            ? "Something went wrong. Please try again."
            : exception.Message;
}

public class RelayCommand : RelayCommand<object?>
{
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : base(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }
}

public class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<T?, bool>? _canExecute;

    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute;
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
        : base(_ => execute(), canExecute is null ? null : _ => canExecute(), onError)
    {
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
        _execute = execute;
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

public enum SmartViewKind
{
    All,
    Today,
    Important,
    Planned
}

public class SmartViewItem : ViewModelBase
{
    private int _count;

    public SmartViewItem(string name, string color, SmartViewKind kind)
    {
        Name = name;
        Color = color;
        Kind = kind;
    }

    public string Name { get; }
    public string Color { get; }
    public SmartViewKind Kind { get; }

    public int Count
    {
        get => _count;
        set => SetProperty(ref _count, value);
    }
}

public class RecurrenceOption
{
    public RecurrenceOption(RepetitionType type) => Type = type;

    public RepetitionType Type { get; }

    public string Name => Type.RepetitionName;

    public override string ToString() => Name;
}

public class PostItemViewModel
{
    private readonly PostFeedItem _feedItem;

    public PostItemViewModel(PostFeedItem feedItem) => _feedItem = feedItem;

    public string AuthorName => _feedItem.ShowName;
    public string AuthorColor => _feedItem.UserColor;
    public DateTime PostDate => _feedItem.PostDate;
    public string PostContent => _feedItem.PostContent;
}
