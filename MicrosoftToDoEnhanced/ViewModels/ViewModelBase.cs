using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MicrosoftToDoEnhanced.ViewModels;

public interface IDialogService
{
    bool Confirm(string title, string message);
    void ShowMessage(string title, string message);
}

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
