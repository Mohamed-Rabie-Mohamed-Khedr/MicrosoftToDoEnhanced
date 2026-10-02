using System.Windows;
using MicrosoftToDoEnhanced.ViewModels;
using MicrosoftToDoEnhanced.Views;

namespace MicrosoftToDoEnhanced.Services;

public sealed class MessageBoxConfirmationService
{
    public bool Confirm(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
        == MessageBoxResult.Yes;
}

public static class GroupDialogService
{
    public static readonly MessageBoxConfirmationService Confirmation = new();

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