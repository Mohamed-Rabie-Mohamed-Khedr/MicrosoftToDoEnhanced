using System;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Core.Repositories;
using MicrosoftToDoEnhanced.Themes;

namespace MicrosoftToDoEnhanced.Views;

public partial class LoginView : Window
{
    private string _selectedAccentHex = "#0078D4";

    public LoginView()
    {
        InitializeComponent();
    }

    private void OnLoginPasswordChanged(object sender, RoutedEventArgs e) =>
        SetViewModelPassword("LoginPassword", ((PasswordBox)sender).Password);

    private void OnRegisterPasswordChanged(object sender, RoutedEventArgs e) =>
        SetViewModelPassword("RegisterPassword", ((PasswordBox)sender).Password);

    private void SetViewModelPassword(string propertyName, string value)
    {
        var property = DataContext?.GetType().GetProperty(propertyName);
        property?.SetValue(DataContext, value);
    }

    private void CreateAnAccountB_Click(object sender, RoutedEventArgs e)
    {
        LoginSP.Visibility = Visibility.Collapsed;
        CreateAnAccountSP.Visibility = Visibility.Visible;
    }

    private void SignInB_Click(object sender, RoutedEventArgs e)
    {
        LoginSP.Visibility = Visibility.Visible;
        CreateAnAccountSP.Visibility = Visibility.Collapsed;
    }

    private void AccentSwatch_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border ring || ring.Tag is not string hex)
            return;

        if (ring.Parent is Panel row)
        {
            foreach (var child in row.Children)
            {
                if (child is Border other)
                    other.BorderBrush = Brushes.Transparent;
            }
        }

        ring.SetResourceReference(Border.BorderBrushProperty, "TextPrimaryBrush");
        _selectedAccentHex = hex;
        ThemeManager.ApplyAccentColor(hex);
    }

    private async void CreateAccountB_Click(object sender, RoutedEventArgs e)
    {
        CreateAccountB.IsEnabled = false;
        UserNameErrorText.Visibility = Visibility.Collapsed;
        DisplayNameErrorText.Visibility = Visibility.Collapsed;
        EmailErrorText.Visibility = Visibility.Collapsed;
        PasswordErrorText.Visibility = Visibility.Collapsed;

        var userName = UserNameTextBox.Text.Trim();
        var showName = RegisterDisplayNameTextBox.Text.Trim();
        var email = RegisterEmailTextBox.Text.Trim();
        var password = RegisterPasswordBox.Password;

        var valid = true;

        if (string.IsNullOrWhiteSpace(showName))
        {
            DisplayNameErrorText.Text = "Display name is required";
            DisplayNameErrorText.Visibility = Visibility.Visible;
            valid = false;
        }

        if (string.IsNullOrWhiteSpace(userName) || userName.Contains(' '))
        {
            ShowUserNameError("User name must not be empty and must not contain spaces");
            valid = false;
        }

        if (string.IsNullOrEmpty(password))
        {
            PasswordErrorText.Text = "Password is required";
            PasswordErrorText.Visibility = Visibility.Visible;
            valid = false;
        }

        if (!string.IsNullOrWhiteSpace(email) && !IsValidEmail(email))
        {
            EmailErrorText.Text = "Please enter a valid email address";
            EmailErrorText.Visibility = Visibility.Visible;
            valid = false;
        }

        if (valid)
        {
            try
            {
                if (await UserRepository.UserExistsAsync(userName))
                {
                    ShowUserNameError("This username is already taken");
                    CreateAccountB.IsEnabled = true;
                    return;
                }

                await UserRepository.AddUserAsync(
                    userName,
                    showName,
                    string.IsNullOrEmpty(email) ? null : email,
                    HashPassword(password),
                    1,
                    _selectedAccentHex);

                UserNameTextBox.Clear();
                RegisterPasswordBox.Clear();
                LoginSP.Visibility = Visibility.Visible;
                CreateAnAccountSP.Visibility = Visibility.Collapsed;
            }
            catch (Exception)
            {
                ShowUserNameError("Could not create your account. Check your connection and try again.");
            }
        }
        CreateAccountB.IsEnabled = true;
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        SIB.IsEnabled = false;
        SignInUserNameErrorText.Visibility = Visibility.Collapsed;
        SignInErrorText.Visibility = Visibility.Collapsed;

        var userName = SignInUserNameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(userName))
        {
            SignInUserNameErrorText.Text = "User name is required";
            SignInUserNameErrorText.Visibility = Visibility.Visible;
            SIB.IsEnabled = true;
            return;
        }

        try
        {
            var storedHash = await UserRepository.GetUserPasswordHashAsync(userName);
            var enteredHash = HashPassword(LoginPasswordBox.Password);

            if (string.IsNullOrEmpty(storedHash) || !string.Equals(storedHash, enteredHash, StringComparison.Ordinal))
            {
                SignInErrorText.Text = "Incorrect username or password";
                SignInErrorText.Visibility = Visibility.Visible;
                SIB.IsEnabled = true;
                return;
            }

            var user = await UserRepository.GetUserByUserNameAsync(userName);
            if (user is null)
            {
                SignInErrorText.Text = "Incorrect username or password";
                SignInErrorText.Visibility = Visibility.Visible;
                SIB.IsEnabled = true;
                return;
            }

            new global::MicrosoftToDoEnhanced.MainWindow(user).Show();
            Close();
        }
        catch (Exception)
        {
            SignInErrorText.Text = "Incorrect username or password";
            SignInErrorText.Visibility = Visibility.Visible;
            SIB.IsEnabled = true;
        }
    }

    private void ShowUserNameError(string message)
    {
        UserNameErrorText.Text = message;
        UserNameErrorText.Visibility = Visibility.Visible;
    }

    private static string HashPassword(string password) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)));

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