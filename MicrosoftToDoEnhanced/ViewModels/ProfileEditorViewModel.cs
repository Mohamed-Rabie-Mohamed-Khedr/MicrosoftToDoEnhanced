using System.Net.Mail;
using System.Windows.Input;
using Core.Repositories;
using MicrosoftToDoEnhanced.Services;
using MicrosoftToDoEnhanced.Themes;
using MicrosoftToDoEnhanced.Views;

namespace MicrosoftToDoEnhanced.ViewModels;

public class ProfileEditorViewModel : ViewModelBase
{
    private readonly User _user;
    private readonly MainViewModel _owner;
    private readonly MessageBoxConfirmationService _confirmation;
    private string _userName;
    private string _showName;
    private string? _userEmail;
    private string _color;

    public ProfileEditorViewModel(
        User user,
        MainViewModel owner,
        MessageBoxConfirmationService? confirmation = null)
    {
        _user = user;
        _owner = owner;
        _confirmation = confirmation ?? new MessageBoxConfirmationService();
        _userName = user.UserName;
        _showName = user.ShowName;
        _userEmail = user.UserEmail;
        _color = string.IsNullOrWhiteSpace(user.Color) ? "#0078D4" : user.Color;

        SaveProfileCommand = new AsyncRelayCommand(SaveProfileAsync, onError: OnCommandError);
        DeleteAccountCommand = new AsyncRelayCommand(DeleteAccountAsync, onError: OnCommandError);
    }

    public event EventHandler? RequestClose;

    public ICommand SaveProfileCommand { get; }
    public ICommand DeleteAccountCommand { get; }

    public string UserName
    {
        get => _userName;
        set => SetProperty(ref _userName, value);
    }

    public string ShowName
    {
        get => _showName;
        set => SetProperty(ref _showName, value);
    }

    public string? UserEmail
    {
        get => _userEmail;
        set => SetProperty(ref _userEmail, value);
    }

    public string Color
    {
        get => _color;
        set => SetProperty(ref _color, value);
    }

    public List<string> ColorChoices { get; } =
    [
        "#0078D4", "#5C2D91", "#D83B01", "#107C10", "#C239B3",
        "#008575", "#8764B8", "#498205", "#CA5010", "#E74856",
        "#00B7C3", "#FF8C00", "#767676", "#000000"
    ];

    private async Task SaveProfileAsync()
    {
        var email = string.IsNullOrWhiteSpace(UserEmail) ? null : UserEmail.Trim();
        if (email is not null && !IsValidEmail(email))
        {
            _owner.RaiseToast("Please enter a valid email address");
            return;
        }

        var result = await UserRepository.UpdateUserInfoAsync(
            _user.UserID, UserName, ShowName, email, Color);

        if (!result.Success)
        {
            _owner.RaiseToast(result.ErrorMessage ?? "Your profile could not be updated.");
            return;
        }

        var oldUserName = _user.UserName;
        var newUserName = UserName.Trim();
        var oldColor = _user.Color;

        _user.UserName = newUserName;
        _user.ShowName = string.IsNullOrWhiteSpace(ShowName) ? newUserName : ShowName.Trim();
        _user.UserEmail = email;
        _user.Color = Color;

        _owner.ApplyUserUpdate();

        if (!string.Equals(oldColor, _user.Color, StringComparison.OrdinalIgnoreCase))
        {
            ThemeManager.ApplyAccentColor(_user.Color);
            ThemeManager.SaveAccent(_user.Color);
        }

        if (!string.Equals(oldUserName, newUserName, StringComparison.Ordinal)
            && RememberedLoginStore.Load() is { } remembered
            && string.Equals(remembered.UserName, oldUserName, StringComparison.Ordinal))
        {
            RememberedLoginStore.Save(newUserName, remembered.Password);
        }

        _owner.RaiseToast("Profile updated");
        Close();
    }

    private async Task DeleteAccountAsync()
    {
        var confirmed = _confirmation.Confirm(
            "Delete account",
            "This permanently deletes your account, your tasks, your groups and everything in them. "
            + "You will be signed out immediately. This cannot be undone. Continue?");
        if (!confirmed)
            return;

        await UserRepository.DeleteUserAccountAsync(_user.UserID);

        Views.RememberedLoginStore.Clear();
        Close();
        _owner.SignOutCommand.Execute(null);
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            _ = new MailAddress(email);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private void Close() => RequestClose?.Invoke(this, EventArgs.Empty);

    private void OnCommandError(Exception exception) =>
        _owner.RaiseToast(DescribeError(exception));
}
