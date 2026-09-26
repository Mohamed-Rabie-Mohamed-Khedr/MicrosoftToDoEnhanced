using System.ComponentModel;
using System.Runtime.CompilerServices;
using MicrosoftToDoEnhanced.Services;

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
            AppLogger.LogError("Unhandled error in a background operation.", exception);
            (onError ?? ViewModelBase.DefaultErrorHandler)?.Invoke(exception);
        }
    }
}

public abstract class ViewModelBase : INotifyPropertyChanged
{
    /// <summary>Used by background work that has no owning view model to report through.</summary>
    public static Action<Exception>? DefaultErrorHandler { get; set; }

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
