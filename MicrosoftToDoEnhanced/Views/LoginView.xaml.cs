using System.Windows;
using System.Windows.Controls;

namespace MicrosoftToDoEnhanced.Views;

public partial class LoginView : Window
{
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

    private void YourAccentColorB_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
    }
}