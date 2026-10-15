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

public class RelayCommand(Action execute, Func<bool>? canExecute = null)
    : RelayCommand<object?>(_ => execute(), canExecute is null ? null : _ => canExecute())
{
}

public class RelayCommand<T>(Action<T?> execute, Func<T?, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) =>
        canExecute?.Invoke(parameter is T value ? value : default) ?? true;

    public void Execute(object? parameter) =>
        execute(parameter is T value ? value : default);
}

public class AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null, Action<Exception>? onError = null)
    : AsyncRelayCommand<object?>(_ => execute(), canExecute is null ? null : _ => canExecute(), onError)
{
}

public class AsyncRelayCommand<T>(Func<T?, Task> execute, Func<T?, bool>? canExecute = null, Action<Exception>? onError = null) : ICommand
{
    private bool _isRunning;

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) =>
        !_isRunning && (canExecute?.Invoke(parameter is T value ? value : default) ?? true);

    public async void Execute(object? parameter)
    {
        if (_isRunning)
            return;

        _isRunning = true;
        RaiseCanExecuteChanged();
        try
        {
            await execute(parameter is T value ? value : default);
        }
        catch (Exception exception)
        {
            onError?.Invoke(exception);
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

public class SmartViewItem(string name, string color, SmartViewKind kind) : ViewModelBase
{
    private int _count;

    public string Name { get; } = name;
    public string Color { get; } = color;
    public SmartViewKind Kind { get; } = kind;

    public int Count
    {
        get => _count;
        set => SetProperty(ref _count, value);
    }
}

public class RecurrenceOption(RepetitionType type)
{
    public RepetitionType Type { get; } = type;

    public string Name => Type.RepetitionName;

    public override string ToString() => Name;
}

public class PostItemViewModel(PostFeedItem feedItem)
{
    public int PostID => feedItem.PostID;
    public string AuthorName => feedItem.ShowName;
    public string AuthorColor => feedItem.UserColor;
    public DateTime PostDate => feedItem.PostDate;
    public string PostContent => feedItem.PostContent;
}
