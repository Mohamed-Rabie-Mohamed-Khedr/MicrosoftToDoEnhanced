using System.ComponentModel;
using System.Net.Mail;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Core.Repositories;
using MicrosoftToDoEnhanced.Services;
using MicrosoftToDoEnhanced.Themes;
using MicrosoftToDoEnhanced.ViewModels;

namespace MicrosoftToDoEnhanced.Views;

public partial class LoginView : Window
{
    public LoginView()
    {
        InitializeComponent();
        DataContext = new LoginViewModel(OpenMainWindow);
        if (DataContext is LoginViewModel viewModel)
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoginViewModel.SelectedAccentHex)
            && sender is LoginViewModel viewModel)
            ThemeManager.ApplyAccentColor(viewModel.SelectedAccentHex);

        if (sender is not LoginViewModel model)
            return;

        if (e.PropertyName == nameof(LoginViewModel.LoginPassword) && model.LoginPassword is null)
            LoginPasswordBox.Password = string.Empty;

        if (e.PropertyName == nameof(LoginViewModel.RegisterPassword) && model.RegisterPassword is null)
            RegisterPasswordBox.Password = string.Empty;
    }

    private void OnLoginPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel viewModel && sender is PasswordBox box)
            viewModel.LoginPassword = box.Password;
    }

    private void OnRegisterPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel viewModel && sender is PasswordBox box)
            viewModel.RegisterPassword = box.Password;
    }

    private void AccentSwatch_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border ring || ring.Tag is not string hex)
            return;
        if (DataContext is LoginViewModel viewModel)
            viewModel.SelectedAccentHex = hex;
    }

    private void OpenMainWindow(User user)
    {
        new global::MicrosoftToDoEnhanced.MainWindow(user).Show();
        Close();
    }
}

public sealed class LoginViewModel : ViewModelBase
{
    private readonly Action<User> _onAuthenticated;

    private string _loginUserName = string.Empty;
    private string? _loginPassword;
    private string? _registerUserName;
    private string? _registerDisplayName;
    private string? _registerEmail;
    private string? _registerPassword;
    private string _selectedAccentHex = "#0078D4";
    private bool _isRegisterMode;
    private bool _isBusy;

    private string? _loginError;
    private string? _loginInfoMessage;
    private string? _loginUserNameError;
    private string? _registerDisplayNameError;
    private string? _registerUserNameError;
    private string? _registerEmailError;
    private string? _registerPasswordError;

    public LoginViewModel(Action<User> onAuthenticated)
    {
        _onAuthenticated = onAuthenticated;

        SignInCommand = new AsyncRelayCommand(SignInAsync, onError: OnCommandError);
        RegisterCommand = new AsyncRelayCommand(RegisterAsync, onError: OnCommandError);
        ToggleModeCommand = new RelayCommand(ToggleMode);
    }

    private void OnCommandError(Exception exception)
    {
        AppLogger.LogError("Login view command", exception);
        LoginError = "Something went wrong. Please try again.";
    }

    public ICommand SignInCommand { get; }
    public ICommand RegisterCommand { get; }
    public ICommand ToggleModeCommand { get; }

    public string LoginUserName
    {
        get => _loginUserName;
        set
        {
            if (SetProperty(ref _loginUserName, value))
                LoginInfoMessage = null;
        }
    }

    public string? LoginPassword
    {
        get => _loginPassword;
        set
        {
            if (SetProperty(ref _loginPassword, value))
            {
                LoginError = null;
                LoginUserNameError = null;
                LoginInfoMessage = null;
            }
        }
    }

    public string? RegisterUserName
    {
        get => _registerUserName;
        set => SetProperty(ref _registerUserName, value);
    }

    public string? RegisterDisplayName
    {
        get => _registerDisplayName;
        set => SetProperty(ref _registerDisplayName, value);
    }

    public string? RegisterEmail
    {
        get => _registerEmail;
        set => SetProperty(ref _registerEmail, value);
    }

    public string? RegisterPassword
    {
        get => _registerPassword;
        set
        {
            if (SetProperty(ref _registerPassword, value))
                RegisterPasswordError = null;
        }
    }

    public string SelectedAccentHex
    {
        get => _selectedAccentHex;
        set => SetProperty(ref _selectedAccentHex, value);
    }

    public bool IsRegisterMode
    {
        get => _isRegisterMode;
        set => SetProperty(ref _isRegisterMode, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string? LoginError
    {
        get => _loginError;
        private set => SetProperty(ref _loginError, value);
    }

    public string? LoginInfoMessage
    {
        get => _loginInfoMessage;
        private set => SetProperty(ref _loginInfoMessage, value);
    }

    public string? LoginUserNameError
    {
        get => _loginUserNameError;
        private set => SetProperty(ref _loginUserNameError, value);
    }

    public string? RegisterDisplayNameError
    {
        get => _registerDisplayNameError;
        private set => SetProperty(ref _registerDisplayNameError, value);
    }

    public string? RegisterUserNameError
    {
        get => _registerUserNameError;
        private set => SetProperty(ref _registerUserNameError, value);
    }

    public string? RegisterEmailError
    {
        get => _registerEmailError;
        private set => SetProperty(ref _registerEmailError, value);
    }

    public string? RegisterPasswordError
    {
        get => _registerPasswordError;
        private set => SetProperty(ref _registerPasswordError, value);
    }

    private void ToggleMode()
    {
        IsRegisterMode = !IsRegisterMode;
        LoginError = null;
        LoginUserNameError = null;
        RegisterDisplayNameError = null;
        RegisterUserNameError = null;
        RegisterEmailError = null;
        RegisterPasswordError = null;
    }

    private async Task SignInAsync()
    {
        IsBusy = true;
        try
        {
            LoginInfoMessage = null;
            LoginError = null;
            LoginUserNameError = null;

            if (string.IsNullOrWhiteSpace(LoginUserName))
            {
                LoginUserNameError = "User name is required";
                return;
            }

            var result = await UserRepository.SignInAsync(LoginUserName, LoginPassword ?? string.Empty);

            LoginPassword = null;

            if (!result.Success)
            {
                LoginError = result.IsThrottled
                    ? $"Too many failed attempts. Try again in {Math.Ceiling(result.RetryAfter.TotalSeconds)} second(s)."
                    : "Incorrect username or password";
                return;
            }

            _onAuthenticated(result.User!);
        }
        catch (Exception exception)
        {
            AppLogger.LogError("Sign in", exception);
            LoginPassword = null;
            LoginError = "Could not sign in. Check the database connection and try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RegisterAsync()
    {
        IsBusy = true;
        try
        {
            RegisterDisplayNameError = null;
            RegisterUserNameError = null;
            RegisterEmailError = null;
            RegisterPasswordError = null;

            var userName = RegisterUserName?.Trim() ?? string.Empty;
            var showName = RegisterDisplayName?.Trim() ?? string.Empty;
            var email = RegisterEmail?.Trim() ?? string.Empty;
            var password = RegisterPassword ?? string.Empty;

            if (string.IsNullOrWhiteSpace(showName))
                RegisterDisplayNameError = "Display name is required";
            else if (showName.Length > DbLimits.MaxShowNameLength)
                RegisterDisplayNameError = $"Display name must be {DbLimits.MaxShowNameLength} characters or fewer";

            if (string.IsNullOrEmpty(userName) || userName.Contains(' '))
                RegisterUserNameError = "User name must not be empty and must not contain spaces";
            else if (userName.Length > DbLimits.MaxUserNameLength)
                RegisterUserNameError = $"User name must be {DbLimits.MaxUserNameLength} characters or fewer";

            if (password.Length < DbLimits.MinPasswordLength)
                RegisterPasswordError = $"Password must be at least {DbLimits.MinPasswordLength} characters";
            else if (password.Length > DbLimits.MaxPasswordLength)
                RegisterPasswordError = $"Password must be {DbLimits.MaxPasswordLength} characters or fewer";

            if (!string.IsNullOrEmpty(email))
            {
                if (email.Length > DbLimits.MaxEmailLength)
                    RegisterEmailError = $"Email must be {DbLimits.MaxEmailLength} characters or fewer";
                else if (!IsValidEmail(email))
                    RegisterEmailError = "Please enter a valid email address";
            }

            if (RegisterDisplayNameError is not null
                || RegisterUserNameError is not null
                || RegisterEmailError is not null
                || RegisterPasswordError is not null)
            {
                return;
            }

            var result = await UserRepository.RegisterAsync(
                userName, showName, string.IsNullOrEmpty(email) ? null : email, password, SelectedAccentHex);

            if (!result.Success)
            {
                RegisterUserNameError = result.ErrorMessage;
                return;
            }

            RegisterUserName = null;
            RegisterDisplayName = null;
            RegisterEmail = null;
            RegisterPassword = null;
            LoginUserName = userName;
            LoginInfoMessage = "Account created — sign in below.";
            IsRegisterMode = false;
        }
        catch (Exception exception)
        {
            AppLogger.LogError("Register", exception);
            RegisterUserNameError = "Could not create your account. Check the database connection and try again.";
        }
        finally
        {
            IsBusy = false;
        }
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
}
