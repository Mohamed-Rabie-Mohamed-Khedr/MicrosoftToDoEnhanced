using System.Windows;
using MicrosoftToDoEnhanced.ViewModels;
using MicrosoftToDoEnhanced.Views;

namespace MicrosoftToDoEnhanced.Services;

public static class ProfileDialogService
{
    private static readonly MessageBoxConfirmationService Confirmation = new();

    public static void ShowEdit(Window ownerWindow, MainViewModel owner)
    {
        var viewModel = new ProfileEditorViewModel(owner.User, owner, Confirmation);
        var window = new ProfileEditorView { Owner = ownerWindow, DataContext = viewModel };
        viewModel.RequestClose += (_, _) => window.Close();

        window.ShowDialog();
    }
}
