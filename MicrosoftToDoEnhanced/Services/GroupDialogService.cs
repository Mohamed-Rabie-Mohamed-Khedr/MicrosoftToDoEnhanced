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
    private static readonly MessageBoxConfirmationService Confirmation = new();

    public static int? ShowCreate(Window ownerWindow, MainViewModel owner)
    {
        var viewModel = new GroupEditorViewModel(owner.User, owner, confirmation: Confirmation);
        var window = new GroupEditorView { Owner = ownerWindow, DataContext = viewModel };
        viewModel.RequestClose += (_, _) => window.Close();

        window.ShowDialog();

        return viewModel.GroupId;
    }

    private static Group? ShowEdit(Window ownerWindow, MainViewModel owner, Group group)
    {
        var viewModel = new GroupEditorViewModel(owner.User, owner, group, Confirmation);
        var window = new GroupEditorView { Owner = ownerWindow, DataContext = viewModel };
        viewModel.RequestClose += (_, _) => window.Close();

        window.ShowDialog();

        return viewModel.Deleted ? null : viewModel.GetUpdatedGroup();
    }

    public static async Task<bool> ShowChatAsync(Window ownerWindow, MainViewModel owner, Group group)
    {
        var viewModel = new GroupChatViewModel(owner.User, group, owner);
        var window = new GroupChatView { Owner = ownerWindow, DataContext = viewModel };
        viewModel.RequestClose += (_, _) => window.Close();
        viewModel.RequestEditGroup += (_, _) =>
        {
            var updated = ShowEdit(window, owner, viewModel.GetGroupSnapshot());
            if (updated is null)
                window.Close();
            else
                viewModel.ApplyGroupUpdate(updated);
        };

        await viewModel.InitializeAsync();
        window.ShowDialog();

        return viewModel.EditTasksRequested;
    }
}
