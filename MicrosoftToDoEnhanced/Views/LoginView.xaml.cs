using System.ComponentModel;
using System.IO;
using System.Net.Mail;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Core.Repositories;
using MicrosoftToDoEnhanced.Themes;
using MicrosoftToDoEnhanced.ViewModels;

namespace MicrosoftToDoEnhanced.Views;

public partial class LoginView : Window
{
    private readonly LoginViewModel _viewModel;

    public LoginView()
    {
        InitializeComponent();
        DataContext = _viewModel = new LoginViewModel(OpenMainWindow);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        if (_viewModel.LoginPassword is not null)
            LoginPasswordBox.Password = _viewModel.LoginPassword;

        if (_viewModel.IsRememberMe)
            Loaded += OnLoadedAutoSignIn;
    }

    private async void OnLoadedAutoSignIn(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedAutoSignIn;
        await _viewModel.SignInAutomaticAsync();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoginViewModel.SelectedAccentHex))
            ThemeManager.ApplyAccentColor(_viewModel.SelectedAccentHex);

        if (e.PropertyName == nameof(LoginViewModel.LoginPassword) && _viewModel.LoginPassword is null)
            LoginPasswordBox.Password = string.Empty;

        if (e.PropertyName == nameof(LoginViewModel.RegisterPassword) && _viewModel.RegisterPassword is null)
            RegisterPasswordBox.Password = string.Empty;
    }

    private void OnLoginPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box)
            _viewModel.LoginPassword = box.Password;
    }

    private void OnRegisterPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box)
            _viewModel.RegisterPassword = box.Password;
    }

    private void OpenMainWindow(User user)
    {
        ThemeManager.ApplyAccentColor(user.Color);
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
    private bool _isRememberMe;

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

        if (RememberedLoginStore.Load() is { } remembered)
        {
            _loginUserName = remembered.UserName;
            _loginPassword = remembered.Password;
            _isRememberMe = true;
        }
    }

    private void OnCommandError(Exception _)
    {
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

    public List<string> AccentChoices { get; } = new()
    {
        "#0078D4", "#5C2D91", "#D83B01", "#107C10", "#C239B3", "#008575"
    };

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

    public bool IsRememberMe
    {
        get => _isRememberMe;
        set
        {
            if (SetProperty(ref _isRememberMe, value) && !value)
                RememberedLoginStore.Clear();
        }
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

    private async Task SignInAsync() => await SignInCoreAsync(isAutomatic: false);

    internal async Task SignInAutomaticAsync() => await SignInCoreAsync(isAutomatic: true);

    private async Task SignInCoreAsync(bool isAutomatic)
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

            var password = LoginPassword ?? string.Empty;
            var result = await UserRepository.SignInAsync(LoginUserName, password);

            LoginPassword = null;

            if (!result.Success)
            {
                LoginError = result.IsThrottled
                    ? $"Too many failed attempts. Try again in {Math.Ceiling(result.RetryAfter.TotalSeconds)} second(s)."
                    : "Incorrect username or password";

                if (isAutomatic)
                    RememberedLoginStore.Clear();

                return;
            }

            if (IsRememberMe)
                RememberedLoginStore.Save(LoginUserName.Trim(), password);
            else
                RememberedLoginStore.Clear();

            _onAuthenticated(result.User!);
        }
        catch (Exception)
        {
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
        catch (Exception)
        {
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

public static class RememberedLoginStore
{
    private const string Description = "MicrosoftToDoEnhanced remembered sign-in";
    private const int CryptProtectUiForbidden = 0x1;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MicrosoftToDoEnhanced", "remember.dat");

    public static (string UserName, string Password)? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;

            return Deserialize(Unprotect(File.ReadAllBytes(FilePath)));
        }
        catch (Exception)
        {
            Clear();
            return null;
        }
    }

    public static void Save(string userName, string password)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllBytes(FilePath, Protect(Serialize(userName, password)));
    }

    public static bool Clear()
    {
        try
        {
            File.Delete(FilePath);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static byte[] Serialize(string userName, string password)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(userName);
            writer.Write(password);
        }

        return stream.ToArray();
    }

    private static (string UserName, string Password)? Deserialize(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream, Encoding.UTF8);

        var userName = reader.ReadString();
        var password = reader.ReadString();

        return userName.Length == 0 ? null : (userName, password);
    }

    private static byte[] Protect(byte[] data)
    {
        var input = default(NativeBlob);
        var output = default(NativeBlob);

        try
        {
            input.Size = data.Length;
            input.Data = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, input.Data, data.Length);

            if (!CryptProtectData(ref input, Description, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CryptProtectUiForbidden, out output))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var protectedData = new byte[output.Size];
            Marshal.Copy(output.Data, protectedData, 0, output.Size);
            return protectedData;
        }
        finally
        {
            if (input.Data != IntPtr.Zero)
                Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero)
                LocalFree(output.Data);
        }
    }

    private static byte[] Unprotect(byte[] data)
    {
        var input = default(NativeBlob);
        var output = default(NativeBlob);

        try
        {
            input.Size = data.Length;
            input.Data = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, input.Data, data.Length);

            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CryptProtectUiForbidden, out output))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var plainData = new byte[output.Size];
            Marshal.Copy(output.Data, plainData, 0, output.Size);
            return plainData;
        }
        finally
        {
            if (input.Data != IntPtr.Zero)
                Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero)
                LocalFree(output.Data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref NativeBlob dataIn, string? dataDescr, IntPtr optionalEntropy, IntPtr reserved,
        IntPtr promptStruct, int flags, out NativeBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(
        ref NativeBlob dataIn, IntPtr dataDescr, IntPtr optionalEntropy, IntPtr reserved,
        IntPtr promptStruct, int flags, out NativeBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
