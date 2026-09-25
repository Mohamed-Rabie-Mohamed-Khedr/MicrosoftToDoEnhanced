using System.Windows;
using MicrosoftToDoEnhanced.ViewModels;
using MicrosoftToDoEnhanced.Views;

namespace MicrosoftToDoEnhanced.Services;

/// <summary>
/// Abstraction for yes/no confirmations so view-models never touch a MessageBox.
/// The view tier supplies the implementation (MessageBoxConfirmationService).
/// </summary>
public interface IConfirmationService
{
    bool Confirm(string title, string message);
}

/// <summary>MessageBox-backed confirmation, wired in at the composition root.</summary>
public sealed class MessageBoxConfirmationService : IConfirmationService
{
    public bool Confirm(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
        == MessageBoxResult.Yes;
}

/// <summary>
/// Opens the group settings dialog (new group or existing group). Data is loaded
/// BEFORE ShowDialog is reached, so the members/posts panels are never rendered empty
/// by a fire-and-forget InitializeAsync racing with the dialog's first redraw.
/// Returns the group id the dialog worked on (a newly created group's id for the
/// create flow), or null when nothing was saved.
/// </summary>
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