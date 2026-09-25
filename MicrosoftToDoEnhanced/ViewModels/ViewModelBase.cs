using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MicrosoftToDoEnhanced.ViewModels;

/// <summary>
/// Abstraction over modal user prompts so view-models never touch MessageBox directly.
/// The view tier supplies the implementation at the composition root.
/// </summary>
public interface IDialogService
{
    bool Confirm(string title, string message);
    void ShowMessage(string title, string message);
}

/// <summary>
/// Fire-and-forget helpers for async void scenarios (property setters, event handlers).
/// </summary>
public static class TaskExtensions
{
    /// <summary>
    /// Awaits <paramref name="task"/> without blocking, forwarding any exception to
    /// <paramref name="onError"/>, or to <see cref="AsyncRelayCommand.ErrorHandler"/> when no
    /// handler is supplied, so failures surface as toasts instead of crashing.
    /// </summary>
    public static async void SafeFireAndForget(this Task task, Action<Exception>? onError = null)
    {
        try
        {
            await task;
        }
        catch (Exception exception)
        {
            (onError ?? AsyncRelayCommand.ErrorHandler)?.Invoke(exception);
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
}