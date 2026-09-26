using System.Windows;
using MicrosoftToDoEnhanced.ViewModels;
using MicrosoftToDoEnhanced.Views;

namespace MicrosoftToDoEnhanced.Services;

public interface IConfirmationService
{
    bool Confirm(string title, string message);
}

public sealed class MessageBoxConfirmationService : IConfirmationService
{
    public bool Confirm(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
        == MessageBoxResult.Yes;
}

public static class GroupDialogService
{
    public static readonly IConfirmationService Confirmation = new MessageBoxConfirmationService();

    public static async Task<int?> ShowAsync(Window ownerWindow, MainViewModel owner, Group? group = null)
    {
        var viewModel = new GroupSettingsViewModel(owner.User, group, owner, Confirmation);
        var window = new GroupSettingsView { Owner = ownerWindow, DataContext = viewModel };
        viewModel.RequestClose += (_, _) => window.Close();

        await viewModel.InitializeAsync();
        window.ShowDialog();

        return viewModel.GroupId;
    }
}
