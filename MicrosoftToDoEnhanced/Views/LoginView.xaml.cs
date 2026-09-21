using System;
using System.Configuration;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Data.SqlClient;
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

    private async void CreateAccount_Click(object sender, RoutedEventArgs e)
    {
        UserNameErrorText.Visibility = Visibility.Collapsed;
        DisplayNameErrorText.Visibility = Visibility.Collapsed;
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

        if (!valid) return;

        var connectionString = ConfigurationManager.ConnectionStrings["MicrosoftToDoEnhanced"]?.ConnectionString;
        if (string.IsNullOrEmpty(connectionString))
        {
            ShowUserNameError("Database connection is not configured.");
            return;
        }

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            using (var existsCommand = new SqlCommand("SELECT dbo.UserExists(@UserName)", connection))
            {
                existsCommand.Parameters.Add("@UserName", SqlDbType.VarChar, 100).Value = userName;
                if (Convert.ToBoolean(await existsCommand.ExecuteScalarAsync()))
                {
                    ShowUserNameError("This username is already taken");
                    return;
                }
            }

            using (var addUserCommand = new SqlCommand("AddUser", connection) { CommandType = CommandType.StoredProcedure })
            {
                addUserCommand.Parameters.Add("@UserName", SqlDbType.VarChar, 100).Value = userName;
                addUserCommand.Parameters.Add("@ShowName", SqlDbType.NVarChar, 100).Value = showName;
                addUserCommand.Parameters.Add("@UserEmail", SqlDbType.VarChar, 255).Value = string.IsNullOrEmpty(email) ? DBNull.Value : email;
                addUserCommand.Parameters.Add("@PasswordHash", SqlDbType.VarChar, 255).Value = HashPassword(password);
                addUserCommand.Parameters.Add("@PermissionID", SqlDbType.Int).Value = 1;
                addUserCommand.Parameters.Add("@Color", SqlDbType.VarChar, 16).Value = _selectedAccentHex;
                await addUserCommand.ExecuteNonQueryAsync();
            }

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

    private void ShowUserNameError(string message)
    {
        UserNameErrorText.Text = message;
        UserNameErrorText.Visibility = Visibility.Visible;
    }

    private static string HashPassword(string password) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
}